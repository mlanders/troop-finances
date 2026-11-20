using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>One invoice ready to print: its payments, and the guardians a scout's invoice is billed to.</summary>
public record InvoiceSheet(InvoiceEntity Invoice, IReadOnlyList<InvoicePaymentEntity> Payments, IReadOnlyList<string> GuardianNames);

/// <summary>One family's combined invoice: who it is billed to and their outstanding invoices,
/// plus money already in the family's scouts' accounts that could pay some of them.</summary>
public record FamilySheet(string BillTo, IReadOnlyList<InvoiceEntity> Invoices)
{
    /// <summary>The family's scouts with money in their scout account.</summary>
    public IReadOnlyList<ScoutFunds> ScoutFunds { get; init; } = [];

    public decimal TotalDue => Invoices.Sum(i => i.AmountDueFromFamily);

    /// <summary>How much of a scout's account could go toward the invoices on this sheet: no more
    /// than the scout has, nor than their own invoices here come to (it can't pay a sibling's or an
    /// adult's).</summary>
    public decimal Usable(ScoutFunds funds) => Math.Min(
        Math.Max(0, funds.Balance),
        Invoices.Where(i => !i.IsForGuardian && i.ScoutId == funds.ScoutId).Sum(i => i.AmountDueFromFamily));

    public decimal DueAfterScoutFunds => TotalDue - ScoutFunds.Sum(Usable);
}

/// <summary>A scout's account balance, as shown on a family bill.</summary>
public record ScoutFunds(string ScoutId, string Name, decimal Balance);

public interface IInvoiceDocumentService
{
    /// <summary>Any invoice, for the treasurer. ownerId (the invoice's OwnerId) makes it a key lookup when known.</summary>
    Task<InvoiceSheet?> GetInvoiceAsync(string invoiceId, string? ownerId = null);

    /// <summary>An invoice for a parent: only one billed to them or to one of their linked scouts, otherwise null.</summary>
    Task<InvoiceSheet?> GetInvoiceForGuardianAsync(GuardianEntity guardian, string invoiceId);

    /// <summary>A guardian's outstanding invoices -- their linked scouts' plus anything billed to them
    /// as an adult -- optionally limited to the given invoice ids. Invoices is empty when nothing is owed.</summary>
    Task<FamilySheet> GetFamilyAsync(GuardianEntity guardian, IReadOnlySet<string>? onlyInvoiceIds = null);

    /// <summary>A whole household's outstanding invoices: its scouts' plus its adults' own.</summary>
    Task<FamilySheet> GetHouseholdAsync(Household household);

    /// <summary>Every outstanding invoice, one sheet per family, in "bill to" order.</summary>
    Task<IReadOnlyList<FamilySheet>> GetAllOutstandingAsync();
}

/// <summary>
/// Gathers what goes on a printed invoice. The PDF itself is drawn by <see cref="InvoicePdf"/>;
/// the endpoints that serve it are in Program.cs.
/// </summary>
public class InvoiceDocumentService(
    IInvoiceRepository invoiceRepository,
    IInvoicePaymentRepository paymentRepository,
    IGuardianScoutLinkRepository linkRepository,
    IGuardianRepository guardianRepository,
    IScoutRepository scoutRepository) : IInvoiceDocumentService
{
    public async Task<InvoiceSheet?> GetInvoiceAsync(string invoiceId, string? ownerId = null)
    {
        var invoice = string.IsNullOrEmpty(ownerId) ? null : await invoiceRepository.GetByIdAsync(ownerId, invoiceId);
        invoice ??= await invoiceRepository.GetByInvoiceIdAsync(invoiceId);
        return invoice is null ? null : await SheetAsync(invoice);
    }

    public async Task<InvoiceSheet?> GetInvoiceForGuardianAsync(GuardianEntity guardian, string invoiceId)
    {
        var invoice = await invoiceRepository.GetByInvoiceIdAsync(invoiceId);
        if (invoice is null)
        {
            return null;
        }

        var isTheirs = invoice.IsForGuardian
            ? invoice.GuardianId == guardian.GuardianId
            : await linkRepository.IsLinkedAsync(guardian.GuardianId, invoice.ScoutId);
        return isTheirs ? await SheetAsync(invoice) : null;
    }

    private async Task<InvoiceSheet> SheetAsync(InvoiceEntity invoice)
    {
        var payments = await paymentRepository.GetByInvoiceAsync(invoice.InvoiceId);
        var guardianNames = new List<string>();
        if (!invoice.IsForGuardian)
        {
            foreach (var link in await linkRepository.GetGuardiansForScoutAsync(invoice.ScoutId))
            {
                if (await guardianRepository.GetByIdAsync(link.GuardianId) is { IsActive: true } guardian)
                {
                    guardianNames.Add(guardian.FullName);
                }
            }
        }

        return new InvoiceSheet(invoice, payments, guardianNames);
    }

    public async Task<FamilySheet> GetFamilyAsync(GuardianEntity guardian, IReadOnlySet<string>? onlyInvoiceIds = null)
    {
        var scoutIds = (await linkRepository.GetScoutsForGuardianAsync(guardian.GuardianId)).Select(l => l.ScoutId);
        var family = await GetHouseholdAsync(Household.Alone(guardian, scoutIds));
        return onlyInvoiceIds is null ? family : family with { Invoices = family.Invoices.Where(i => onlyInvoiceIds.Contains(i.InvoiceId)).ToList() };
    }

    public async Task<FamilySheet> GetHouseholdAsync(Household household)
    {
        var invoices = new List<InvoiceEntity>();
        var funds = new List<ScoutFunds>();
        foreach (var scoutId in household.ScoutIds)
        {
            invoices.AddRange(await invoiceRepository.GetByScoutAsync(scoutId));
            if (await scoutRepository.GetByIdAsync(scoutId) is { CachedBalance: > 0 } scout)
            {
                funds.Add(new ScoutFunds(scout.ScoutId, scout.FullName, scout.CachedBalance));
            }
        }

        // Plus anything billed to the adults themselves.
        foreach (var guardian in household.Guardians)
        {
            invoices.AddRange(await invoiceRepository.GetByGuardianAsync(guardian.GuardianId));
        }

        return new FamilySheet(household.Name, Ordered(invoices.Where(i => i.StatusValue == InvoiceStatus.Outstanding)))
        {
            ScoutFunds = funds.OrderBy(f => f.Name).ToList(),
        };
    }

    public async Task<IReadOnlyList<FamilySheet>> GetAllOutstandingAsync()
    {
        var outstanding = (await invoiceRepository.GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, int.MaxValue)).Items;
        var outstandingByScout = outstanding.Where(i => !i.IsForGuardian)
            .GroupBy(i => i.ScoutId)
            .ToDictionary(g => g.Key, g => g.ToList());
        // Invoices billed to an adult, by that adult's guardian id.
        var outstandingByAdult = outstanding.Where(i => i.IsForGuardian)
            .GroupBy(i => i.GuardianId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var scouts = (await scoutRepository.GetAllAsync()).ToDictionary(s => s.ScoutId);
        List<ScoutFunds> FundsOf(IEnumerable<string> scoutIds) => scoutIds
            .Select(id => scouts.GetValueOrDefault(id))
            .OfType<ScoutEntity>()
            .Where(s => s.CachedBalance > 0)
            .Select(s => new ScoutFunds(s.ScoutId, s.FullName, s.CachedBalance))
            .OrderBy(f => f.Name)
            .ToList();

        var families = new List<FamilySheet>();
        foreach (var household in await Households.BuildAsync((await guardianRepository.GetAllAsync()).Where(g => g.IsActive), linkRepository))
        {
            var invoices = household.ScoutIds.SelectMany(id => outstandingByScout.Remove(id, out var found) ? found : [])
                .Concat(household.Guardians.SelectMany(g => outstandingByAdult.Remove(g.GuardianId, out var own) ? own : []))
                .ToList();
            if (invoices.Count > 0)
            {
                families.Add(new FamilySheet(household.Name, Ordered(invoices)) { ScoutFunds = FundsOf(household.ScoutIds) });
            }
        }

        // Whatever is left belongs to scouts with no active guardian linked (one sheet per scout),
        // or to an adult who is no longer an active guardian (one sheet each).
        families.AddRange(outstandingByScout.Values.Select(invoices =>
            new FamilySheet($"Family of {invoices[0].ScoutName}", Ordered(invoices)) { ScoutFunds = FundsOf([invoices[0].ScoutId]) }));
        families.AddRange(outstandingByAdult.Values.Select(invoices => new FamilySheet(invoices[0].BilledTo, Ordered(invoices))));

        return families.OrderBy(f => f.BillTo).ToList();
    }

    private static List<InvoiceEntity> Ordered(IEnumerable<InvoiceEntity> invoices) =>
        invoices.OrderBy(i => i.DueDate ?? DateTimeOffset.MaxValue).ThenBy(i => i.BilledTo).ToList();
}
