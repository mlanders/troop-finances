using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>When a trip's cost goes up after someone has paid: a top-up invoice for the difference.</summary>
public class TripTopUpTests
{
    private readonly InMemorySignInAppRepository _repository = new();
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IScoutAccountService _accounts = Substitute.For<IScoutAccountService>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly Dictionary<string, InvoiceEntity> _issued = [];
    private int _next;

    public TripTopUpTests()
    {
        List<ScoutEntity> scouts = [new() { RowKey = "s-sam", FullName = "Sam Patel" }];
        List<GuardianEntity> guardians = [new() { RowKey = "pat@x", GuardianId = "g-pat", FullName = "Pat Lee" }];
        _scouts.GetAllAsync().Returns(scouts);
        _scouts.GetByIdAsync(Arg.Any<string>()).Returns(c => scouts.FirstOrDefault(s => s.ScoutId == c.Arg<string>()));
        _guardians.GetAllAsync().Returns(guardians);
        _guardians.GetByIdAsync(Arg.Any<string>()).Returns(c => guardians.FirstOrDefault(g => g.GuardianId == c.Arg<string>()));
        _invoices.GetByIdAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(c => _issued.GetValueOrDefault(c.ArgAt<string>(1)));
        _accounts.IssueInvoiceAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => Issue(c.ArgAt<string>(0), c.ArgAt<decimal>(1)));
        _accounts.IssueGuardianInvoiceAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(c => Issue(c.ArgAt<string>(0), c.ArgAt<decimal>(1)));
        _accounts.When(a => a.VoidInvoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
            .Do(c => _issued[c.ArgAt<string>(1)].StatusValue = InvoiceStatus.Voided);
    }

    private InvoiceEntity Issue(string ownerId, decimal amount)
    {
        var id = $"inv{++_next}";
        return _issued[id] = new InvoiceEntity
        {
            PartitionKey = ownerId, RowKey = id, InvoiceId = id, TotalAmount = amount, AmountDueFromFamily = amount, StatusValue = InvoiceStatus.Outstanding,
        };
    }

    private void Pay(string invoiceId, decimal amount)
    {
        var invoice = _issued[invoiceId];
        invoice.AmountDueFromFamily -= amount;
        invoice.StatusValue = invoice.AmountDueFromFamily == 0 ? InvoiceStatus.Paid : InvoiceStatus.Outstanding;
    }

    private SignInAppService Service() =>
        new(_repository, _scouts, _guardians, _invoices, _accounts, _audit, new PassThroughUnitOfWork(), TimeProvider.System);

    private static TripUpload Trip(params TripUploadLine[] lines) =>
        new("Catalina", "Dec 4 – 6", new DateOnly(2099, 12, 4), new DateOnly(2099, 12, 6), null, "Leader", lines);

    private static TripUploadLine Sam(decimal amount, bool adult = false) =>
        new("e1", adult ? "Pat Lee" : "Sam Patel", adult ? "p-pat" : "p-sam", adult ? SignInPersonType.Adult : SignInPersonType.Scout, amount, "");

    /// <summary>Sam is invoiced $40 for the trip and pays some of it.</summary>
    private async Task<SignInAppService> InvoicedAndPaidAsync(decimal paid, bool adult = false)
    {
        var service = Service();
        await service.ReceiveRosterAsync([new("p-sam", "Sam Patel", SignInPersonType.Scout, true), new("p-pat", "Pat Lee", SignInPersonType.Adult, true)]);
        await service.LinkAsync("p-sam", "s-sam", notBilled: false, "Treasurer");
        await service.LinkAsync("p-pat", "g-pat", notBilled: false, "Treasurer");
        await service.ReceiveTripAsync("t1", Trip(Sam(40, adult)));
        await service.ApplyAsync("t1", "Treasurer");
        if (paid > 0)
        {
            Pay("inv1", paid);
        }

        return service;
    }

    private async Task<TripReviewLine> LineAsync(SignInAppService service) => Assert.Single((await service.GetReviewAsync("t1"))!.Lines);

    [Fact]
    public async Task A_rise_after_paying_issues_a_top_up_for_the_difference()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));

        var before = await LineAsync(service);
        Assert.Equal(TripLineAction.TopUp, before.Action);
        Assert.Contains("a top-up invoice for $320.00", before.Note);

        Assert.Equal(1, await service.ApplyAsync("t1", "Treasurer"));

        await _accounts.Received(1).IssueInvoiceAsync("s-sam", 320, 0, Arg.Any<DateTimeOffset>(), "Catalina (Dec 4 – 6): cost went up to $360.00", "Treasurer");
        await _accounts.DidNotReceiveWithAnyArgs().VoidInvoiceAsync(default!, default!, default!, default!);
        var after = await LineAsync(service);
        Assert.Equal((TripLineAction.UpToDate, 360m, 340m, 20m), (after.Action, after.InvoicedTotal, after.Due, after.PaidSoFar));
        Assert.Equal(["inv1", "inv2"], after.Invoices.Select(i => i.InvoiceId));
    }

    [Fact]
    public async Task An_adults_top_up_goes_to_their_guardian_account()
    {
        var service = await InvoicedAndPaidAsync(paid: 40, adult: true);
        await service.ReceiveTripAsync("t1", Trip(Sam(450, adult: true)));

        await service.ApplyAsync("t1", "Treasurer");

        await _accounts.Received(1).IssueGuardianInvoiceAsync("g-pat", 410, Arg.Any<DateTimeOffset>(), Arg.Any<string>(), "Treasurer");
    }

    [Fact]
    public async Task A_second_rise_gets_a_second_top_up_for_just_the_new_difference()
    {
        var service = await InvoicedAndPaidAsync(paid: 40);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");
        await service.ReceiveTripAsync("t1", Trip(Sam(375)));

        await service.ApplyAsync("t1", "Treasurer");

        await _accounts.Received(1).IssueInvoiceAsync("s-sam", 15, 0, Arg.Any<DateTimeOffset>(), Arg.Any<string>(), "Treasurer");
        Assert.Equal(3, (await LineAsync(service)).Invoices.Count);
    }

    [Fact]
    public async Task A_drop_after_paying_is_still_left_for_the_treasurer()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(30)));

        var line = await LineAsync(service);

        Assert.Equal(TripLineAction.NeedsAttention, line.Action);
        Assert.Contains("went down from $40.00 to $30.00", line.Note);
    }

    [Fact]
    public async Task With_nothing_paid_a_change_voids_every_invoice_and_issues_one_at_the_new_cost()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");
        // The treasurer refunds Sam, so nothing's paid on either invoice now.
        _issued["inv1"].AmountDueFromFamily = 40;
        _issued["inv1"].StatusValue = InvoiceStatus.Outstanding;
        await service.ReceiveTripAsync("t1", Trip(Sam(300)));

        Assert.Equal(TripLineAction.Reissue, (await LineAsync(service)).Action);
        await service.ApplyAsync("t1", "Treasurer");

        Assert.Equal(InvoiceStatus.Voided, _issued["inv1"].StatusValue);
        Assert.Equal(InvoiceStatus.Voided, _issued["inv2"].StatusValue);
        var line = await LineAsync(service);
        Assert.Equal(("inv3", 300m), (line.Invoice!.InvoiceId, line.InvoicedTotal));
        Assert.Null(line.Line.TopUpInvoiceIds);
    }

    [Fact]
    public async Task Dropping_out_voids_the_trip_invoice_and_top_up_when_nothing_was_paid_on_either()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");
        Pay("inv1", -20);   // refunded
        await service.ReceiveTripAsync("t1", Trip());

        await service.ApplyAsync("t1", "Treasurer");

        Assert.All(_issued.Values, i => Assert.Equal(InvoiceStatus.Voided, i.StatusValue));
    }

    [Fact]
    public async Task Dropping_out_after_paying_with_a_top_up_needs_the_treasurer()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");
        await service.ReceiveTripAsync("t1", Trip());

        var line = await LineAsync(service);

        Assert.Equal(TripLineAction.NeedsAttention, line.Action);
        Assert.Contains("Refund or void the invoices by hand", line.Note);
    }

    [Fact]
    public async Task The_sign_in_app_sees_what_is_owed_across_both_invoices_and_paid_only_when_both_are()
    {
        var service = await InvoicedAndPaidAsync(paid: 40);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");

        Assert.Equal(new TripLineStatusDto("e1", true, TripLineStatus.Outstanding, 360, 320, 40), Assert.Single(await service.GetStatusAsync("t1")));

        Pay("inv2", 320);
        Assert.Equal(TripLineStatus.Paid, Assert.Single(await service.GetStatusAsync("t1")).Status);
    }

    [Fact]
    public async Task A_payment_is_split_between_the_trip_invoice_and_the_top_up()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");

        var result = await service.RecordPaymentAsync("t1", new TripPaymentUpload("pay1", "e1", 100, InvoicePaymentMethod.Check, "1234", "Leader"));

        Assert.Equal(TripPaymentOutcome.Recorded, result.Outcome);
        await _accounts.Received(1).RecordInvoicePaymentAsync("s-sam", "inv1", 20, InvoicePaymentMethod.Check, "1234", "Leader");
        await _accounts.Received(1).RecordInvoicePaymentAsync("s-sam", "inv2", 80, InvoicePaymentMethod.Check, "1234", "Leader");
        Assert.Equal("inv1", _repository.Payments[("t1", "pay1")].InvoiceId);
    }

    [Fact]
    public async Task Once_the_trip_invoice_is_paid_payments_go_to_the_top_up()
    {
        var service = await InvoicedAndPaidAsync(paid: 40);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");

        await service.RecordPaymentAsync("t1", new TripPaymentUpload("pay1", "e1", 320, InvoicePaymentMethod.Cash, null, "Leader"));

        await _accounts.Received(1).RecordInvoicePaymentAsync("s-sam", "inv2", 320, InvoicePaymentMethod.Cash, null, "Leader");
    }

    [Fact]
    public async Task More_than_is_owed_in_all_still_goes_to_the_ledger_to_refuse()
    {
        var service = await InvoicedAndPaidAsync(paid: 20);
        await service.ReceiveTripAsync("t1", Trip(Sam(360)));
        await service.ApplyAsync("t1", "Treasurer");

        await service.RecordPaymentAsync("t1", new TripPaymentUpload("pay1", "e1", 500, InvoicePaymentMethod.Cash, null, "Leader"));

        await _accounts.Received(1).RecordInvoicePaymentAsync("s-sam", "inv1", 500, InvoicePaymentMethod.Cash, null, "Leader");
    }
}
