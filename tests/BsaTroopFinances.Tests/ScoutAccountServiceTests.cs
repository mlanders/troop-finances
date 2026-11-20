using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The real ScoutAccountService with faked storage: fundraiser allocations, manual
/// adjustments, marking an invoice paid, renaming and deleting a scout.</summary>
public class ScoutAccountServiceTests
{
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly IScoutLedgerRepository _ledger = Substitute.For<IScoutLedgerRepository>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IInvoicePaymentRepository _payments = Substitute.For<IInvoicePaymentRepository>();
    private readonly IFundraiserEventRepository _fundraisers = Substitute.For<IFundraiserEventRepository>();
    private readonly IGuardianScoutLinkRepository _links = Substitute.For<IGuardianScoutLinkRepository>();
    private readonly IReimbursementRequestRepository _reimbursements = Substitute.For<IReimbursementRequestRepository>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly ScoutAccountService _service;

    private readonly ScoutEntity _alex = new() { PartitionKey = "Scout", RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 30 };

    public ScoutAccountServiceTests()
    {
        _service = new ScoutAccountService(
            _scouts, _ledger, _invoices, _payments, _fundraisers, _links, Substitute.For<IGuardianRepository>(),
            _reimbursements, _audit, Substitute.For<INotificationService>(), TimeProvider.System, new PassThroughUnitOfWork());
        _scouts.GetByIdAsync("alex").Returns(_alex);
        _ledger.GetForSourceAsync(Arg.Any<string>(), Arg.Any<string>()).Returns([]);
        _ledger.GetForScoutAsync(Arg.Any<string>()).Returns([]);
        _invoices.GetByScoutAsync(Arg.Any<string>()).Returns([]);
        _links.GetGuardiansForScoutAsync(Arg.Any<string>()).Returns([]);
        _reimbursements.GetByScoutAsync(Arg.Any<string>()).Returns([]);
    }

    private List<ScoutLedgerEntryEntity> Appended() =>
        _ledger.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IScoutLedgerRepository.AppendEntryAsync))
            .Select(c => (ScoutLedgerEntryEntity)c.GetArguments()[0]!).ToList();

    private void Fundraiser(decimal? raised, decimal? expenses, params decimal[] alreadyAllocated)
    {
        _fundraisers.GetByIdAsync("popcorn").Returns(new FundraiserEventEntity
        {
            PartitionKey = FundraiserEventEntity.PartitionKeyValue, RowKey = "popcorn", Name = "Popcorn sale", TotalRaised = raised, TotalExpenses = expenses,
        });
        _ledger.GetForSourceAsync(LedgerSourceType.FundraiserAllocation, "popcorn").Returns(
            alreadyAllocated.Select(a => new ScoutLedgerEntryEntity { Amount = a, SourceTypeValue = LedgerSourceType.FundraiserAllocation, SourceId = "popcorn" }).ToList());
    }

    // ---- Fundraiser allocations

    [Fact]
    public async Task An_allocation_credits_the_scout_and_is_audited_on_the_fundraiser()
    {
        Fundraiser(raised: 1000, expenses: 200);

        await _service.ApplyFundraiserAllocationAsync("alex", "popcorn", 150, "Popcorn share", "Pat", hoursWorked: 6);

        var entry = Assert.Single(Appended());
        Assert.Equal(("alex", LedgerEntryType.Credit, LedgerSourceType.FundraiserAllocation, "popcorn", 150m, (decimal?)6m),
            (entry.PartitionKey, entry.EntryTypeValue, entry.SourceTypeValue, entry.SourceId, entry.Amount, entry.HoursWorked));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Fundraiser, "popcorn", "Proceeds Allocated", "Pat", "Popcorn share",
            Arg.Is<string?>(d => d!.Contains("Alex Rivera") && d.Contains("6 hours")));
    }

    [Fact]
    public async Task Allocations_can_use_up_exactly_the_net_proceeds_but_no_more()
    {
        Fundraiser(raised: 1000, expenses: 200, 500, 250);

        await _service.ApplyFundraiserAllocationAsync("alex", "popcorn", 50, "Last share", "Pat");
        Fundraiser(raised: 1000, expenses: 200, 500, 250, 50);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApplyFundraiserAllocationAsync("alex", "popcorn", 0.01m, "One cent too many", "Pat"));

        Assert.Contains("exceed", refused.Message);
        Assert.Single(Appended());
    }

    [Fact]
    public async Task Nothing_can_be_allocated_before_the_fundraisers_total_is_entered()
    {
        Fundraiser(raised: null, expenses: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApplyFundraiserAllocationAsync("alex", "popcorn", 10, "Too soon", "Pat"));

        Assert.Empty(Appended());
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-10, null)]
    [InlineData(10, 0)]
    [InlineData(10, -2)]
    public async Task An_allocation_needs_a_positive_amount_and_positive_hours_if_any(decimal amount, int? hours)
    {
        Fundraiser(raised: 1000, expenses: 0);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.ApplyFundraiserAllocationAsync("alex", "popcorn", amount, "Share", "Pat", hours));

        Assert.Empty(Appended());
    }

    [Fact]
    public async Task An_allocation_from_a_fundraiser_that_isnt_there_is_refused() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApplyFundraiserAllocationAsync("alex", "nope", 10, "Share", "Pat"));

    // ---- Manual adjustments

    [Theory]
    [InlineData(25, LedgerEntryType.Credit)]
    [InlineData(-25, LedgerEntryType.Debit)]
    public async Task A_manual_adjustment_is_a_credit_or_debit_of_its_size(decimal amount, string type)
    {
        await _service.ApplyManualAdjustmentAsync("alex", amount, "Correcting a duplicate", "Pat");

        var entry = Assert.Single(Appended());
        Assert.Equal((type, 25m, LedgerSourceType.ManualAdjustment, "Correcting a duplicate", "Pat"),
            (entry.EntryTypeValue, entry.Amount, entry.SourceTypeValue, entry.Description, entry.CreatedBy));
    }

    [Theory]
    [InlineData(25, "Credited $25.00 to Alex Rivera.")]
    [InlineData(-25, "Debited $25.00 from Alex Rivera.")]
    public async Task A_manual_adjustment_is_in_the_activity_history_with_its_reason(decimal amount, string details)
    {
        await _service.ApplyManualAdjustmentAsync("alex", amount, "Correcting a duplicate", "Pat");

        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Scout, "alex", "Manual Adjustment", "Pat", "Correcting a duplicate", details);
    }

    [Fact]
    public async Task A_refused_manual_adjustment_leaves_no_history()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.ApplyManualAdjustmentAsync("alex", 0, "Nothing", "Pat"));

        await _audit.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task A_manual_adjustment_may_take_a_balance_below_zero()
    {
        await _service.ApplyManualAdjustmentAsync("alex", -500, "Covering a bounced check", "Pat");

        Assert.Equal(500m, Assert.Single(Appended()).Amount);
    }

    [Theory]
    [InlineData(0, "A reason")]
    [InlineData(10, "")]
    [InlineData(10, "   ")]
    public async Task A_manual_adjustment_needs_an_amount_and_a_reason(decimal amount, string reason)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _service.ApplyManualAdjustmentAsync("alex", amount, reason, "Pat"));

        Assert.Empty(Appended());
    }

    // ---- Marking an invoice paid

    [Fact]
    public async Task Marking_an_invoice_paid_records_a_check_for_whatever_is_still_due()
    {
        var invoice = new InvoiceEntity
        {
            PartitionKey = "alex", RowKey = "inv1", InvoiceId = "inv1", ScoutId = "alex", ScoutName = "Alex Rivera", Description = "Camp",
            TotalAmount = 100, AmountDueFromFamily = 60, StatusValue = InvoiceStatus.Outstanding,
        };
        _invoices.GetByIdAsync("alex", "inv1").Returns(invoice);

        await _service.MarkInvoicePaidAsync("alex", "inv1", "Pat");

        var payment = (InvoicePaymentEntity)Assert.Single(_payments.ReceivedCalls()).GetArguments()[0]!;
        Assert.Equal((60m, InvoicePaymentMethod.Check, "Manual mark paid", "Pat"), (payment.Amount, payment.Method, payment.Reference, payment.RecordedBy));
        Assert.Equal((0m, InvoiceStatus.Paid), (invoice.AmountDueFromFamily, invoice.StatusValue));
    }

    [Fact]
    public async Task Marking_an_invoice_that_isnt_there_paid_is_refused() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.MarkInvoicePaidAsync("alex", "nope", "Pat"));

    // ---- Renaming

    [Fact]
    public async Task A_rename_updates_every_copy_of_the_name_and_leaves_a_zero_entry_in_the_history()
    {
        var link = new GuardianScoutLinkEntity { GuardianId = "maria", ScoutId = "alex", ScoutName = "Alex Rivera" };
        var invoice = new InvoiceEntity { PartitionKey = "alex", RowKey = "inv1", InvoiceId = "inv1", ScoutId = "alex", ScoutName = "Alex Rivera" };
        var request = new ReimbursementRequestEntity { RowKey = "req1", RequestId = "req1", ScoutId = "alex", ScoutName = "Alex Rivera" };
        _links.GetGuardiansForScoutAsync("alex").Returns([link]);
        _invoices.GetByScoutAsync("alex").Returns([invoice]);
        _reimbursements.GetByScoutAsync("alex").Returns([request]);

        await _service.RenameScoutAsync("alex", "  Alexandra Rivera ", "Pat");

        Assert.Equal("Alexandra Rivera", _alex.FullName);
        Assert.All(new[] { link.ScoutName, invoice.ScoutName, request.ScoutName }, name => Assert.Equal("Alexandra Rivera", name));
        await _scouts.Received(1).UpdateAsync(_alex);
        await _links.Received(1).AddLinkAsync(link);
        await _invoices.Received(1).UpdateAsync(invoice);
        await _reimbursements.Received(1).UpdateAsync(request);
        var entry = Assert.Single(Appended());
        Assert.Equal((LedgerSourceType.NameChange, 0m, "Name changed: Alex Rivera → Alexandra Rivera"), (entry.SourceTypeValue, entry.Amount, entry.Description));
    }

    [Fact]
    public async Task Renaming_to_the_same_name_changes_nothing()
    {
        await _service.RenameScoutAsync("alex", "Alex Rivera ", "Pat");

        await _scouts.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
        Assert.Empty(Appended());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_scout_cant_be_renamed_to_nothing(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenameScoutAsync("alex", name, "Pat"));

        Assert.Equal("Alex Rivera", _alex.FullName);
    }

    // ---- Deleting

    [Fact]
    public async Task A_scout_with_no_activity_is_deleted_with_their_guardian_links()
    {
        _links.GetGuardiansForScoutAsync("alex").Returns([new GuardianScoutLinkEntity { GuardianId = "maria", ScoutId = "alex" }]);

        await _service.DeleteScoutAsync("alex", "Pat");

        await _links.Received(1).RemoveLinkAsync("maria", "alex");
        await _scouts.Received(1).DeleteAsync(_alex);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Scout, "alex", "Deleted", "Pat", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task A_scout_with_an_invoice_cant_be_deleted()
    {
        _invoices.GetByScoutAsync("alex").Returns([new InvoiceEntity { InvoiceId = "inv1", ScoutId = "alex" }]);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteScoutAsync("alex", "Pat"));

        Assert.Contains("can't be deleted", refused.Message);
        await _scouts.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
    }

    [Fact]
    public async Task A_scout_with_ledger_history_cant_be_deleted()
    {
        _ledger.GetForScoutAsync("alex").Returns([new ScoutLedgerEntryEntity { Amount = 0, SourceTypeValue = LedgerSourceType.NameChange }]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteScoutAsync("alex", "Pat"));

        await _scouts.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
        await _links.DidNotReceiveWithAnyArgs().RemoveLinkAsync(default!, default!);
    }
}
