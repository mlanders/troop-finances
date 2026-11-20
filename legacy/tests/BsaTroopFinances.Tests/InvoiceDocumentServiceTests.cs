using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>What goes on a printed invoice: who it is billed to, which invoices a family sheet
/// carries, how families are grouped, and what a parent is allowed to print.</summary>
public class InvoiceDocumentServiceTests
{
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoicePaymentRepository _payments = Substitute.For<IInvoicePaymentRepository>();
    private readonly IGuardianScoutLinkRepository _links = Substitute.For<IGuardianScoutLinkRepository>();
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly InvoiceDocumentService _service;

    private static readonly GuardianEntity Maria = new() { GuardianId = "maria", FullName = "Maria Rivera", IsActive = true };

    public InvoiceDocumentServiceTests()
    {
        _service = new InvoiceDocumentService(_invoices, _payments, _links, _guardians, _scouts);
        _payments.GetByInvoiceAsync(Arg.Any<string>()).Returns(new List<InvoicePaymentEntity>());
        _links.GetScoutsForGuardianAsync(Arg.Any<string>()).Returns(new List<GuardianScoutLinkEntity>());
        _links.GetGuardiansForScoutAsync(Arg.Any<string>()).Returns(new List<GuardianScoutLinkEntity>());
        _invoices.GetByScoutAsync(Arg.Any<string>()).Returns(new List<InvoiceEntity>());
        _invoices.GetByGuardianAsync(Arg.Any<string>()).Returns(new List<InvoiceEntity>());

        // The Rivera family: Maria is linked to Alex and Casey.
        _guardians.GetByIdAsync("maria").Returns(Maria);
        _links.GetScoutsForGuardianAsync("maria").Returns(Links("alex", "casey"));
        _links.IsLinkedAsync("maria", "alex").Returns(true);
        _invoices.GetByScoutAsync("alex").Returns(new List<InvoiceEntity> { Invoice("inv-camp", "alex", "Alex Rivera", "Summer camp deposit", 175) });
        _invoices.GetByScoutAsync("casey").Returns(new List<InvoiceEntity>
        {
            Invoice("inv-food", "casey", "Casey Rivera", "Campout food", 45),
            Invoice("inv-flag", "casey", "Casey Rivera", "Patrol flag", 18),
            Invoice("inv-old", "casey", "Casey Rivera", "Old dues", 0, InvoiceStatus.Paid),
        });
    }

    private static List<GuardianScoutLinkEntity> Links(params string[] scoutIds) =>
        scoutIds.Select(id => new GuardianScoutLinkEntity { ScoutId = id }).ToList();

    private static InvoiceEntity Invoice(string id, string scoutId, string scoutName, string description, decimal due, string status = InvoiceStatus.Outstanding) => new()
    {
        PartitionKey = scoutId, RowKey = id, InvoiceId = id, ScoutId = scoutId, ScoutName = scoutName, Description = description,
        TotalAmount = due, AmountDueFromFamily = due, StatusValue = status,
        IssuedUtc = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero), DueDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static InvoiceEntity Adult(string id, string guardianId, string name, decimal due) => new()
    {
        PartitionKey = guardianId, RowKey = id, InvoiceId = id, GuardianId = guardianId, GuardianName = name, Description = "Summer camp, adult",
        TotalAmount = due, AmountDueFromFamily = due, StatusValue = InvoiceStatus.Outstanding, IssuedUtc = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero),
    };

    private void AllOutstandingIs(params InvoiceEntity[] invoices) =>
        _invoices.GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, int.MaxValue).Returns(new PagedResult<InvoiceEntity>(invoices, invoices.Length));

    [Fact]
    public async Task A_scouts_invoice_is_billed_to_the_scouts_active_guardians_and_carries_its_payments()
    {
        var invoice = Invoice("inv1", "alex", "Alex Rivera", "Summer camp deposit", 125);
        _invoices.GetByIdAsync("alex", "inv1").Returns(invoice);
        _links.GetGuardiansForScoutAsync("alex").Returns(new List<GuardianScoutLinkEntity> { new() { GuardianId = "maria" }, new() { GuardianId = "gone" } });
        _guardians.GetByIdAsync("gone").Returns(new GuardianEntity { GuardianId = "gone", FullName = "Former Guardian", IsActive = false });
        var payment = new InvoicePaymentEntity { Amount = 50, Method = InvoicePaymentMethod.Venmo, Reference = "@rivera-family" };
        _payments.GetByInvoiceAsync("inv1").Returns(new List<InvoicePaymentEntity> { payment });

        var sheet = await _service.GetInvoiceAsync("inv1", "alex");

        Assert.Same(invoice, sheet!.Invoice);
        Assert.Equal(["Maria Rivera"], sheet.GuardianNames);
        Assert.Equal([payment], sheet.Payments);
    }

    [Fact]
    public async Task An_adult_invoice_needs_no_guardian_lookup_and_a_missing_invoice_gives_nothing()
    {
        _invoices.GetByInvoiceIdAsync("inv9").Returns(Adult("inv9", "maria", "Maria Rivera", 320));

        var sheet = await _service.GetInvoiceAsync("inv9");       // no owner hint: found by invoice id

        Assert.Equal("Maria Rivera", sheet!.Invoice.BilledTo);
        Assert.Empty(sheet.GuardianNames);
        await _links.DidNotReceiveWithAnyArgs().GetGuardiansForScoutAsync(default!);
        Assert.Null(await _service.GetInvoiceAsync("nope", "alex"));
    }

    [Fact]
    public async Task A_family_sheet_holds_every_outstanding_invoice_for_the_scouts_and_the_adult()
    {
        _invoices.GetByGuardianAsync("maria").Returns(new List<InvoiceEntity> { Adult("inv-adult", "maria", "Maria Rivera", 320) });

        var family = await _service.GetFamilyAsync(Maria);

        Assert.Equal("Maria Rivera", family.BillTo);
        Assert.Equal(["inv-adult", "inv-camp", "inv-flag", "inv-food"], family.Invoices.Select(i => i.InvoiceId).Order());
        Assert.Equal(558m, family.Invoices.Sum(i => i.AmountDueFromFamily));        // "Old dues" is paid, so left off
    }

    [Fact]
    public async Task A_family_sheet_shows_scout_account_money_that_can_pay_each_scouts_own_invoices()
    {
        _invoices.GetByGuardianAsync("maria").Returns(new List<InvoiceEntity> { Adult("inv-adult", "maria", "Maria Rivera", 320) });
        // Alex has $40 toward a $175 invoice; Casey has $100 but only $63 of invoices to use it on.
        _scouts.GetByIdAsync("alex").Returns(new ScoutEntity { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 40m });
        _scouts.GetByIdAsync("casey").Returns(new ScoutEntity { RowKey = "casey", FullName = "Casey Rivera", CachedBalance = 100m });

        var family = await _service.GetFamilyAsync(Maria);

        Assert.Equal([("Alex Rivera", 40m, 40m), ("Casey Rivera", 100m, 63m)],
            family.ScoutFunds.Select(f => (f.Name, f.Balance, family.Usable(f))));
        Assert.Equal((558m, 455m), (family.TotalDue, family.DueAfterScoutFunds));   // the adult's $320 can't use scout money
    }

    [Fact]
    public async Task Scouts_with_nothing_in_their_account_are_left_off()
    {
        _scouts.GetByIdAsync("alex").Returns(new ScoutEntity { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 0m });
        _scouts.GetByIdAsync("casey").Returns(new ScoutEntity { RowKey = "casey", FullName = "Casey Rivera", CachedBalance = -5m });

        var family = await _service.GetFamilyAsync(Maria);

        Assert.Empty(family.ScoutFunds);
        Assert.Equal(family.TotalDue, family.DueAfterScoutFunds);
    }

    [Fact]
    public async Task A_family_sheet_can_be_limited_to_the_ticked_invoices()
    {
        var family = await _service.GetFamilyAsync(Maria, new HashSet<string> { "inv-camp", "inv-flag", "inv-old" });

        Assert.Equal(["inv-camp", "inv-flag"], family.Invoices.Select(i => i.InvoiceId).Order());
        Assert.Equal(193m, family.Invoices.Sum(i => i.AmountDueFromFamily));
    }

    [Fact]
    public async Task Printing_all_outstanding_gives_one_sheet_per_family_with_shared_scouts_billed_once()
    {
        // Maria and Luis are both linked to Alex and Casey; Chris has Jordan; Sam has no active
        // guardian; the scout linked to Dana owes nothing.
        _guardians.GetAllAsync().Returns(new List<GuardianEntity>
        {
            Maria,
            new() { GuardianId = "luis", FullName = "Luis Rivera", IsActive = true },
            new() { GuardianId = "chris", FullName = "Chris Lee", IsActive = true },
            new() { GuardianId = "dana", FullName = "Dana Nothing-Owed", IsActive = true },
            new() { GuardianId = "gone", FullName = "Former Guardian", IsActive = false },
        });
        _links.GetScoutsForGuardianAsync("luis").Returns(Links("casey"));
        _links.GetScoutsForGuardianAsync("chris").Returns(Links("jordan"));
        _links.GetScoutsForGuardianAsync("dana").Returns(Links("paidup"));
        _links.GetScoutsForGuardianAsync("gone").Returns(Links("sam"));
        AllOutstandingIs(
            Invoice("inv-camp", "alex", "Alex Rivera", "Summer camp deposit", 175),
            Invoice("inv-food", "casey", "Casey Rivera", "Campout food", 45),
            Invoice("inv-shirt", "jordan", "Jordan Lee", "Troop T-shirt", 20),
            Invoice("inv-dues", "sam", "Sam Patel", "Annual dues", 60));

        var families = await _service.GetAllOutstandingAsync();

        Assert.Equal(["Chris Lee", "Family of Sam Patel", "Luis Rivera and Maria Rivera"], families.Select(f => f.BillTo));
        Assert.Equal([20m, 60m, 220m], families.Select(f => f.Invoices.Sum(i => i.AmountDueFromFamily)));
        Assert.Equal(2, families[2].Invoices.Count);        // both Rivera scouts, once
    }

    [Fact]
    public async Task Adult_invoices_print_with_their_household_or_alone_when_the_adult_has_no_scouts()
    {
        _guardians.GetAllAsync().Returns(new List<GuardianEntity> { Maria, new() { GuardianId = "lee", FullName = "Leader With No Scout", IsActive = true } });
        AllOutstandingIs(
            Invoice("inv-camp", "alex", "Alex Rivera", "Summer camp deposit", 175),
            Adult("inv-a1", "maria", "Maria Rivera", 320),
            Adult("inv-a2", "lee", "Leader With No Scout", 320),
            Adult("inv-a3", "left", "Former Leader", 40));           // no longer a guardian at all

        var families = await _service.GetAllOutstandingAsync();

        Assert.Equal(["Former Leader", "Leader With No Scout", "Maria Rivera"], families.Select(f => f.BillTo));
        Assert.Equal([40m, 320m, 495m], families.Select(f => f.Invoices.Sum(i => i.AmountDueFromFamily)));
    }

    [Fact]
    public async Task Nothing_outstanding_means_no_sheets()
    {
        _guardians.GetAllAsync().Returns(new List<GuardianEntity> { Maria });
        AllOutstandingIs();

        Assert.Empty(await _service.GetAllOutstandingAsync());
    }

    [Theory]
    [InlineData("own", true)]             // billed to the parent as an adult
    [InlineData("scout", true)]           // billed to their linked scout
    [InlineData("other-adult", false)]
    [InlineData("other-scout", false)]
    [InlineData("missing", false)]
    public async Task A_parent_can_print_their_own_and_their_scouts_invoices_and_nobody_elses(string invoiceId, bool allowed)
    {
        _invoices.GetByInvoiceIdAsync("own").Returns(Adult("own", "maria", "Maria Rivera", 60));
        _invoices.GetByInvoiceIdAsync("scout").Returns(Invoice("scout", "alex", "Alex Rivera", "Summer camp deposit", 175));
        _invoices.GetByInvoiceIdAsync("other-adult").Returns(Adult("other-adult", "pat", "Pat Jones", 40));
        _invoices.GetByInvoiceIdAsync("other-scout").Returns(Invoice("other-scout", "stranger", "Sam Stranger", "Not theirs", 99));

        var sheet = await _service.GetInvoiceForGuardianAsync(Maria, invoiceId);

        Assert.Equal(allowed, sheet is not null);
    }
}
