using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>One household on the Families page.</summary>
public record FamilyRow(Household Family, IReadOnlyList<ScoutEntity> Scouts, decimal ScoutFunds, decimal AmountOwed, bool HasAdultCharges)
{
    /// <summary>Identifies the household in a link (?g=): its first guardian's id.</summary>
    public string Key => Family.Guardians[0].GuardianId;

    /// <summary>False for a guardian with no scouts who has never been invoiced: nothing to print.</summary>
    public bool HasActivity => Scouts.Count > 0 || HasAdultCharges;
}

/// <summary>A scout's fund over the statement period. Entries are the ones dated in the period;
/// the opening balance is everything before it.</summary>
public record ScoutFundStatus(ScoutEntity Scout, decimal OpeningBalance, IReadOnlyList<ScoutLedgerEntryEntity> Entries, decimal AmountOwed)
{
    public decimal MoneyIn => Entries.Where(e => e.SignedAmount > 0).Sum(e => e.SignedAmount);
    public decimal MoneyOut => -Entries.Where(e => e.SignedAmount < 0).Sum(e => e.SignedAmount);
    public decimal ClosingBalance => OpeningBalance + MoneyIn - MoneyOut;
}

/// <summary>A payment with what it was for ("Alex Rivera — Summer camp deposit").</summary>
public record StatusPaymentLine(InvoicePaymentEntity Payment, string For);

/// <summary>Everything on one family's status sheet.</summary>
public record FamilyStatusSheet(
    Household Family, DateOnly From, DateOnly To, IReadOnlyList<ScoutFundStatus> Scouts,
    decimal AdultAmountOwed, IReadOnlyList<InvoiceEntity> Invoices, IReadOnlyList<StatusPaymentLine> Payments)
{
    public decimal AmountDue => Scouts.Sum(s => s.AmountOwed) + AdultAmountOwed;
}

public interface IFamilyStatusService
{
    /// <summary>Every household, in name order, for the Families page. Inactive guardians are left
    /// out unless asked for.</summary>
    Task<IReadOnlyList<FamilyRow>> GetFamiliesAsync(bool includeInactive);

    /// <summary>The households these guardians belong to, each once, in name order. A guardian picked
    /// here is included even when inactive.</summary>
    Task<IReadOnlyList<Household>> ResolveAsync(IReadOnlyCollection<string> guardianIds);

    /// <summary>A parent's own household for the portal: just them and their own linked scouts.</summary>
    Task<Household> GetOwnHouseholdAsync(GuardianEntity guardian);

    Task<FamilyStatusSheet> GetSheetAsync(Household family, DateOnly from, DateOnly to);
}

/// <summary>
/// Gathers a family's financial picture for a period: each scout's fund activity, the family's
/// invoices and the payments they made. The PDF is drawn by <see cref="InvoicePdf.FamilyStatus"/>.
/// </summary>
public class FamilyStatusService(
    IGuardianRepository guardianRepository,
    IGuardianScoutLinkRepository linkRepository,
    IScoutRepository scoutRepository,
    IScoutLedgerRepository ledgerRepository,
    IInvoiceRepository invoiceRepository,
    IInvoicePaymentRepository paymentRepository) : IFamilyStatusService
{
    /// <summary>The period to print: as given, or the last 12 months up to today. Reversed dates are
    /// swapped rather than refused.</summary>
    public static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var end = to ?? today;
        var start = from ?? end.AddMonths(-12);
        return start <= end ? (start, end) : (end, start);
    }

    public async Task<IReadOnlyList<FamilyRow>> GetFamiliesAsync(bool includeInactive)
    {
        var guardians = (await guardianRepository.GetAllAsync()).Where(g => includeInactive || g.IsActive);
        var households = await Households.BuildAsync(guardians, linkRepository);
        var scouts = (await scoutRepository.GetAllAsync()).ToDictionary(s => s.ScoutId);
        var outstanding = (await invoiceRepository.GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, int.MaxValue)).Items;
        var billedAdults = await invoiceRepository.GetGuardianIdsWithInvoicesAsync();

        return households.Select(household =>
        {
            var guardianIds = household.Guardians.Select(g => g.GuardianId).ToHashSet();
            var scoutIds = household.ScoutIds.ToHashSet();
            var familyScouts = household.ScoutIds.Select(id => scouts.GetValueOrDefault(id)).OfType<ScoutEntity>().OrderBy(s => s.FullName).ToList();
            var owed = outstanding
                .Where(i => i.IsForGuardian ? guardianIds.Contains(i.GuardianId!) : scoutIds.Contains(i.ScoutId))
                .Sum(i => i.AmountDueFromFamily);
            return new FamilyRow(household, familyScouts, familyScouts.Sum(s => s.CachedBalance), owed, guardianIds.Overlaps(billedAdults));
        }).OrderBy(r => r.Family.Name).ToList();
    }

    public async Task<IReadOnlyList<Household>> ResolveAsync(IReadOnlyCollection<string> guardianIds)
    {
        var wanted = guardianIds.ToHashSet();
        var guardians = (await guardianRepository.GetAllAsync()).Where(g => g.IsActive || wanted.Contains(g.GuardianId));
        return (await Households.BuildAsync(guardians, linkRepository))
            .Where(h => h.Guardians.Any(g => wanted.Contains(g.GuardianId)))
            .OrderBy(h => h.Name)
            .ToList();
    }

    public async Task<Household> GetOwnHouseholdAsync(GuardianEntity guardian) =>
        Household.Alone(guardian, (await linkRepository.GetScoutsForGuardianAsync(guardian.GuardianId)).Select(l => l.ScoutId));

    public async Task<FamilyStatusSheet> GetSheetAsync(Household family, DateOnly from, DateOnly to)
    {
        var invoices = new List<InvoiceEntity>();
        var funds = new List<ScoutFundStatus>();
        foreach (var scoutId in family.ScoutIds)
        {
            var scout = await scoutRepository.GetByIdAsync(scoutId);
            if (scout is null)
            {
                continue;
            }

            // Renames are recorded with a zero amount; they say nothing about money.
            var ledger = (await ledgerRepository.GetForScoutAsync(scoutId)).Where(e => e.Amount != 0).OrderBy(e => e.RowKey).ToList();
            var scoutInvoices = await invoiceRepository.GetByScoutAsync(scoutId);
            invoices.AddRange(scoutInvoices);
            funds.Add(new ScoutFundStatus(
                scout,
                ledger.Where(e => Day(e.CreatedUtc) < from).Sum(e => e.SignedAmount),
                ledger.Where(e => Day(e.CreatedUtc) >= from && Day(e.CreatedUtc) <= to).ToList(),
                Owed(scoutInvoices)));
        }

        var adultInvoices = new List<InvoiceEntity>();
        foreach (var guardian in family.Guardians)
        {
            adultInvoices.AddRange(await invoiceRepository.GetByGuardianAsync(guardian.GuardianId));
        }

        invoices.AddRange(adultInvoices);

        var shown = invoices.Where(i => i.StatusValue == InvoiceStatus.Outstanding || WasOpenDuring(i, from, to)).OrderBy(i => i.IssuedUtc).ToList();

        // Money the family paid in the period. Payments from scout funds already show as fund
        // activity, so they aren't counted twice.
        var payments = new List<StatusPaymentLine>();
        foreach (var invoice in shown)
        {
            foreach (var payment in await paymentRepository.GetByInvoiceAsync(invoice.InvoiceId))
            {
                if (payment.Method != InvoicePaymentMethod.ScoutFunds && Day(payment.PaidUtc) >= from && Day(payment.PaidUtc) <= to)
                {
                    payments.Add(new StatusPaymentLine(payment, $"{invoice.BilledTo} — {invoice.Description}"));
                }
            }
        }

        return new FamilyStatusSheet(family, from, to, funds.OrderBy(f => f.Scout.FullName).ToList(),
            Owed(adultInvoices), shown, payments.OrderBy(p => p.Payment.PaidUtc).ToList());
    }

    /// <summary>A paid or voided invoice belongs on the sheet if it was open at some point in the
    /// period: issued by the end and not settled before the start. (Outstanding ones always do.)</summary>
    private static bool WasOpenDuring(InvoiceEntity invoice, DateOnly from, DateOnly to) =>
        Day(invoice.IssuedUtc) <= to && Day(invoice.PaidUtc ?? invoice.VoidedUtc ?? invoice.IssuedUtc) >= from;

    private static decimal Owed(IEnumerable<InvoiceEntity> invoices) =>
        invoices.Where(i => i.StatusValue == InvoiceStatus.Outstanding).Sum(i => i.AmountDueFromFamily);

    /// <summary>The calendar day a moment falls on, in the server's time zone -- the same day the
    /// invoice PDFs print for it.</summary>
    private static DateOnly Day(DateTimeOffset moment) => DateOnly.FromDateTime(moment.LocalDateTime);
}
