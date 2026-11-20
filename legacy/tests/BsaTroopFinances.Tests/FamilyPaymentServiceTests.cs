using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The real ScoutAccountService with faked storage, so these exercise the actual payment logic.</summary>
public class FamilyPaymentServiceTests
{
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoicePaymentRepository _payments = Substitute.For<IInvoicePaymentRepository>();
    private readonly IScoutLedgerRepository _ledger = Substitute.For<IScoutLedgerRepository>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly ScoutAccountService _service;

    private readonly InvoiceEntity _campAlex = Invoice("inv-camp", "alex", "Alex Rivera", "Summer camp deposit", due: 175);
    private readonly InvoiceEntity _foodCasey = Invoice("inv-food", "casey", "Casey Rivera", "Campout food", due: 45);
    private readonly InvoiceEntity _flagCasey = Invoice("inv-flag", "casey", "Casey Rivera", "Patrol flag", due: 18);

    public FamilyPaymentServiceTests()
    {
        _service = new ScoutAccountService(
            _scouts, _ledger, _invoices, _payments,
            Substitute.For<IFundraiserEventRepository>(), Substitute.For<IGuardianScoutLinkRepository>(), _guardians,
            Substitute.For<IReimbursementRequestRepository>(), _audit, _notifications,
            TimeProvider.System, new PassThroughUnitOfWork());
        _guardians.GetByIdAsync("maria").Returns(new GuardianEntity { GuardianId = "maria", FullName = "Maria Rivera", IsActive = true });

        foreach (var invoice in new[] { _campAlex, _foodCasey, _flagCasey })
        {
            _invoices.GetByIdAsync(invoice.ScoutId, invoice.InvoiceId).Returns(invoice);
        }
    }

    private static InvoiceEntity Invoice(string id, string scoutId, string scoutName, string description, decimal due) => new()
    {
        PartitionKey = scoutId,
        RowKey = id,
        InvoiceId = id,
        ScoutId = scoutId,
        ScoutName = scoutName,
        Description = description,
        TotalAmount = due,
        AmountDueFromFamily = due,
        StatusValue = InvoiceStatus.Outstanding,
    };

    [Fact]
    public async Task An_invoice_billed_to_an_adult_is_owed_in_full_with_no_scout_and_no_scout_funds()
    {
        var due = new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var invoice = await _service.IssueGuardianInvoiceAsync("maria", 320m, due, "Summer camp, adult", "Pat Treasurer");

        Assert.True(invoice.IsForGuardian);
        Assert.Equal(("maria", "Maria Rivera", "Maria Rivera"), (invoice.GuardianId, invoice.GuardianName, invoice.BilledTo));
        Assert.Equal(("", "", "maria"), (invoice.ScoutId, invoice.ScoutName, invoice.OwnerId));
        Assert.Equal((320m, 0m, 320m, InvoiceStatus.Outstanding), (invoice.TotalAmount, invoice.AmountAppliedFromAccount, invoice.AmountDueFromFamily, invoice.StatusValue));
        await _invoices.Received(1).AddAsync(invoice);
        await _ledger.DidNotReceiveWithAnyArgs().AppendEntryAsync(default!);
        await _notifications.Received(1).InvoiceIssuedAsync(invoice);

        // It can be paid like any other invoice, including as part of a family payment...
        _invoices.GetByIdAsync("maria", invoice.InvoiceId).Returns(invoice);
        await _service.RecordFamilyPaymentAsync(
            [new("maria", invoice.InvoiceId, 100m), new("alex", "inv-camp", 175m)], InvoicePaymentMethod.Check, "Check 7", "Pat Treasurer");
        Assert.Equal(220m, invoice.AmountDueFromFamily);

        // ...but never from a scout's account.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RecordInvoicePaymentAsync("maria", invoice.InvoiceId, 50m, InvoicePaymentMethod.ScoutFunds, null, "Pat Treasurer"));
        Assert.Contains("billed to an adult", ex.Message);
        await _ledger.DidNotReceiveWithAnyArgs().AppendEntryAsync(default!);
    }

    [Fact]
    public async Task An_adult_invoice_needs_a_real_guardian()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.IssueGuardianInvoiceAsync("nobody", 50m, DateTimeOffset.UtcNow, "Camp", "Pat Treasurer"));
        await _invoices.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    private List<InvoicePaymentEntity> RecordedPayments() =>
        _payments.ReceivedCalls().Select(c => (InvoicePaymentEntity)c.GetArguments()[0]!).ToList();

    [Fact]
    public async Task One_check_across_three_invoices_records_a_linked_payment_on_each()
    {
        var groupId = await _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m), new("casey", "inv-food", 45m), new("casey", "inv-flag", 10m)],
            InvoicePaymentMethod.Check, "Check 1042", "Pat Treasurer");

        var payments = RecordedPayments();
        Assert.Equal(3, payments.Count);
        Assert.All(payments, p =>
        {
            Assert.Equal(groupId, p.PaymentGroupId);
            Assert.Equal(230, p.GroupTotal);
            Assert.Equal(InvoicePaymentMethod.Check, p.Method);
            Assert.Equal("Check 1042", p.Reference);
        });
        Assert.Equal([175m, 45m, 10m], payments.Select(p => p.Amount));

        // Fully paid invoices close; the partial one stays outstanding with the rest due.
        Assert.Equal(InvoiceStatus.Paid, _campAlex.StatusValue);
        Assert.Equal(InvoiceStatus.Paid, _foodCasey.StatusValue);
        Assert.Equal(InvoiceStatus.Outstanding, _flagCasey.StatusValue);
        Assert.Equal(8, _flagCasey.AmountDueFromFamily);

        // A check never touches scout funds.
        Assert.Empty(_ledger.ReceivedCalls());
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Invoice, "inv-flag", "Payment Recorded", "Pat Treasurer",
            "Check 1042", Arg.Is<string>(d => d.Contains("Family payment: $10.00 of $230.00 across 3 invoices")));
    }

    [Fact]
    public async Task An_amount_over_what_is_due_rejects_the_whole_payment_and_writes_nothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m), new("casey", "inv-food", 50m)], InvoicePaymentMethod.Check, "Check 1042", "Pat Treasurer"));

        Assert.Contains("more than the $45.00 still due", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task An_invoice_that_is_no_longer_outstanding_rejects_the_whole_payment()
    {
        _foodCasey.StatusValue = InvoiceStatus.Voided;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m), new("casey", "inv-food", 45m)], InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        Assert.Contains("voided, not outstanding", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task The_same_invoice_twice_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 100m), new("alex", "inv-camp", 75m)], InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        AssertNothingWritten();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Zero_or_negative_amounts_are_rejected(int amount)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", amount)], InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        AssertNothingWritten();
    }

    [Fact]
    public async Task A_failure_partway_stops_the_family_payment_and_reports_the_error()
    {
        // With a real database the transaction rolls back everything (see SqlTransactionTests);
        // here the fakes just show the failure surfaces and later invoices aren't attempted.
        _invoices.UpdateAsync(Arg.Is<InvoiceEntity>(i => i.InvoiceId == "inv-food"))
            .Returns(_ => Task.FromException(new InvalidOperationException("storage hiccup")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m), new("casey", "inv-food", 45m), new("casey", "inv-flag", 18m)], InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        Assert.Equal("storage hiccup", ex.Message);
        Assert.DoesNotContain(RecordedPayments(), p => p.InvoiceId == "inv-flag");
    }

    [Fact]
    public async Task Single_invoice_payments_are_unchanged_and_not_grouped()
    {
        await _service.RecordInvoicePaymentAsync("alex", "inv-camp", 50m, InvoicePaymentMethod.Check, "Check 7", "Pat Treasurer");

        var payment = Assert.Single(RecordedPayments());
        Assert.Equal(50, payment.Amount);
        Assert.Equal(string.Empty, payment.PaymentGroupId);
        Assert.Equal(0, payment.GroupTotal);
        Assert.Equal(125, _campAlex.AmountDueFromFamily);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Invoice, "inv-camp", "Payment Recorded", "Pat Treasurer",
            "Check 7", "$50.00 received via Check; remaining due $125.00.");
    }

    [Theory]
    [InlineData(InvoicePaymentMethod.Cash, null)]
    [InlineData(InvoicePaymentMethod.Venmo, "@rivera-family")]
    [InlineData(InvoicePaymentMethod.Other, "Zelle")]
    public async Task Family_payments_record_the_chosen_method(string method, string? reference)
    {
        await _service.RecordFamilyPaymentAsync([new("alex", "inv-camp", 175m), new("casey", "inv-food", 45m)], method, reference, "Pat Treasurer");

        Assert.All(RecordedPayments(), p => Assert.Equal(method, p.Method));
        Assert.Empty(_ledger.ReceivedCalls());
    }

    [Fact]
    public async Task Other_needs_a_description_of_how_it_was_paid()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m)], InvoicePaymentMethod.Other, "  ", "Pat Treasurer"));

        Assert.Contains("Describe how", ex.Message);
        AssertNothingWritten();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordInvoicePaymentAsync(
            "alex", "inv-camp", 50m, InvoicePaymentMethod.Other, null, "Pat Treasurer"));
    }

    private void GiveBalances(decimal alex, decimal casey)
    {
        _scouts.GetByIdAsync("alex").Returns(new ScoutEntity { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = alex });
        _scouts.GetByIdAsync("casey").Returns(new ScoutEntity { RowKey = "casey", FullName = "Casey Rivera", CachedBalance = casey });
    }

    private List<ScoutLedgerEntryEntity> LedgerEntries() =>
        _ledger.ReceivedCalls().Select(c => (ScoutLedgerEntryEntity)c.GetArguments()[0]!).ToList();

    [Fact]
    public async Task Part_from_the_scout_account_and_the_rest_by_check()
    {
        GiveBalances(alex: 40m, casey: 0m);

        var groupId = await _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 135m, FromScoutAccount: 40m), new("casey", "inv-food", 45m)],
            InvoicePaymentMethod.Check, "Check 1042", "Pat Treasurer");

        var payments = RecordedPayments();
        // The scout-account part is its own payment, outside the check's group, so a deposit never counts it.
        var fromAccount = Assert.Single(payments, p => p.Method == InvoicePaymentMethod.ScoutFunds);
        Assert.Equal(("inv-camp", 40m, "", 0m), (fromAccount.InvoiceId, fromAccount.Amount, fromAccount.PaymentGroupId, fromAccount.GroupTotal));
        var check = payments.Where(p => p.Method == InvoicePaymentMethod.Check).ToList();
        Assert.Equal([135m, 45m], check.Select(p => p.Amount));
        Assert.All(check, p => Assert.Equal((groupId, 180m), (p.PaymentGroupId, p.GroupTotal)));

        var debit = Assert.Single(LedgerEntries());
        Assert.Equal(("alex", LedgerEntryType.Debit, 40m), (debit.PartitionKey, debit.EntryTypeValue, debit.Amount));
        Assert.Equal((InvoiceStatus.Paid, 40m), (_campAlex.StatusValue, _campAlex.AmountAppliedFromAccount));
    }

    [Fact]
    public async Task Siblings_each_pay_from_their_own_account_and_no_method_is_needed_when_that_covers_it()
    {
        GiveBalances(alex: 200m, casey: 100m);

        await _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 0m, FromScoutAccount: 175m), new("casey", "inv-food", 0m, FromScoutAccount: 45m)],
            method: "", reference: null, "Pat Treasurer");

        Assert.All(RecordedPayments(), p => Assert.Equal(InvoicePaymentMethod.ScoutFunds, p.Method));
        Assert.Equal([("alex", 175m), ("casey", 45m)], LedgerEntries().Select(e => (e.PartitionKey, e.Amount)));
        Assert.Equal((InvoiceStatus.Paid, InvoiceStatus.Paid), (_campAlex.StatusValue, _foodCasey.StatusValue));
    }

    [Fact]
    public async Task A_scout_can_not_be_taken_below_zero_even_across_two_invoices()
    {
        GiveBalances(alex: 0m, casey: 50m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordFamilyPaymentAsync(
            [new("casey", "inv-food", 5m, FromScoutAccount: 40m), new("casey", "inv-flag", 0m, FromScoutAccount: 18m)],
            InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        Assert.Contains("$58.00 from Casey Rivera's scout account is more than the $50.00", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Both_parts_together_can_not_be_more_than_is_due()
    {
        GiveBalances(alex: 100m, casey: 0m);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 150m, FromScoutAccount: 50m)], InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        AssertNothingWritten();
    }

    [Fact]
    public async Task Scout_funds_can_not_pay_an_adults_invoice_in_a_family_payment()
    {
        var adult = new InvoiceEntity
        {
            PartitionKey = "maria", RowKey = "inv-adult", InvoiceId = "inv-adult", GuardianId = "maria", GuardianName = "Maria Rivera",
            Description = "Adult camp", TotalAmount = 100m, AmountDueFromFamily = 100m, StatusValue = InvoiceStatus.Outstanding,
        };
        _invoices.GetByIdAsync("maria", "inv-adult").Returns(adult);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordFamilyPaymentAsync(
            [new("maria", "inv-adult", 60m, FromScoutAccount: 40m)], InvoicePaymentMethod.Check, null, "Pat Treasurer"));

        Assert.Contains("Scout funds can't pay Maria Rivera's own invoice", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Scout_funds_is_not_a_method_for_the_familys_own_part()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordFamilyPaymentAsync(
            [new("alex", "inv-camp", 175m)], InvoicePaymentMethod.ScoutFunds, null, "Pat Treasurer"));

        AssertNothingWritten();
    }

    [Theory]
    [InlineData(InvoicePaymentMethod.Cash)]
    [InlineData(InvoicePaymentMethod.Venmo)]
    public async Task Single_invoice_payments_accept_the_new_methods(string method)
    {
        await _service.RecordInvoicePaymentAsync("alex", "inv-camp", 50m, method, null, "Pat Treasurer");

        Assert.Equal(method, Assert.Single(RecordedPayments()).Method);
    }

    private void AssertNothingWritten()
    {
        Assert.Empty(_payments.ReceivedCalls());
        _invoices.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        Assert.Empty(_ledger.ReceivedCalls());
    }
}
