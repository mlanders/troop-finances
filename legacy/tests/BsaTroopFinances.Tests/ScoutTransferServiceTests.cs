using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>Moving money off a scout's account, and deactivating a scout, with the real
/// ScoutAccountService over faked storage.</summary>
public class ScoutTransferServiceTests
{
    private readonly IScoutRepository _scouts = Substitute.For<IScoutRepository>();
    private readonly IScoutLedgerRepository _ledger = Substitute.For<IScoutLedgerRepository>();
    private readonly IFinanceAuditRepository _audit = Substitute.For<IFinanceAuditRepository>();
    private readonly List<ScoutLedgerEntryEntity> _entries = [];
    private readonly ScoutAccountService _service;

    private readonly ScoutEntity _alex = new() { RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 120m, IsActive = true };
    private readonly ScoutEntity _casey = new() { RowKey = "casey", FullName = "Casey Rivera", CachedBalance = 10m, IsActive = true };

    public ScoutTransferServiceTests()
    {
        _service = new ScoutAccountService(
            _scouts, _ledger, Substitute.For<IInvoiceRepository>(), Substitute.For<IInvoicePaymentRepository>(),
            Substitute.For<IFundraiserEventRepository>(), Substitute.For<IGuardianScoutLinkRepository>(), Substitute.For<IGuardianRepository>(),
            Substitute.For<IReimbursementRequestRepository>(), _audit, Substitute.For<INotificationService>(),
            TimeProvider.System, new PassThroughUnitOfWork());
        _scouts.GetByIdAsync("alex").Returns(_ => _alex);
        _scouts.GetByIdAsync("casey").Returns(_ => _casey);

        // Like the real repository: the entry is kept and the scout's cached balance moves with it.
        _ledger.AppendEntryAsync(Arg.Do<ScoutLedgerEntryEntity>(e =>
        {
            _entries.Add(e);
            var scout = e.PartitionKey == "alex" ? _alex : _casey;
            scout.CachedBalance += e.EntryTypeValue == LedgerEntryType.Credit ? e.Amount : -e.Amount;
        })).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Moving_to_another_scout_debits_one_and_credits_the_other_as_one_transfer()
    {
        await _service.TransferBalanceAsync("alex", BalanceDestination.ToScout("casey"), 50m, " Family asked ", "Pat");

        Assert.Equal((70m, 60m), (_alex.CachedBalance, _casey.CachedBalance));
        Assert.Equal(2, _entries.Count);
        Assert.All(_entries, e => Assert.Equal(LedgerSourceType.Transfer, e.SourceTypeValue));
        Assert.Single(_entries.Select(e => e.SourceId).Distinct());
        Assert.Equal("To Casey Rivera: Family asked", _entries[0].Description);
        Assert.Equal("From Alex Rivera: Family asked", _entries[1].Description);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Scout, "alex", "Funds Transferred", "Pat", Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Moving_to_the_general_fund_only_debits_the_scout()
    {
        await _service.TransferBalanceAsync("alex", BalanceDestination.GeneralFund, 120m, "Aged out", "Pat");

        var entry = Assert.Single(_entries);
        Assert.Equal((LedgerEntryType.Debit, 120m, "To the troop general fund: Aged out"), (entry.EntryTypeValue, entry.Amount, entry.Description));
        Assert.Equal(0m, _alex.CachedBalance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("120.01")]
    public async Task More_than_the_balance_or_nothing_cant_be_moved(string amount)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.TransferBalanceAsync("alex", BalanceDestination.GeneralFund, decimal.Parse(amount), "Aged out", "Pat"));

        Assert.Empty(_entries);
    }

    [Fact]
    public async Task A_reason_and_a_different_scout_are_required()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.TransferBalanceAsync("alex", BalanceDestination.GeneralFund, 10m, "  ", "Pat"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.TransferBalanceAsync("alex", BalanceDestination.ToScout("alex"), 10m, "Oops", "Pat"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.TransferBalanceAsync("alex", BalanceDestination.ToScout("gone"), 10m, "Sibling", "Pat"));

        Assert.Empty(_entries);
    }

    [Fact]
    public async Task Deactivating_can_move_the_whole_balance_first()
    {
        await _service.DeactivateAsync("alex", BalanceDestination.ToScout("casey"), "Aged out; to his brother", "Pat");

        Assert.False(_alex.IsActive);
        Assert.Equal((0m, 130m), (_alex.CachedBalance, _casey.CachedBalance));
        await _scouts.Received(1).UpdateAsync(Arg.Is<ScoutEntity>(s => s.ScoutId == "alex" && !s.IsActive));
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Scout, "alex", "Deactivated", "Pat",
            Arg.Any<string?>(), Arg.Is<string?>(d => d!.Contains("$120.00") && d.Contains("Casey Rivera")));
    }

    [Fact]
    public async Task Deactivating_without_a_destination_leaves_the_balance_on_the_account()
    {
        await _service.DeactivateAsync("alex", null, null, "Pat");

        Assert.False(_alex.IsActive);
        Assert.Equal(120m, _alex.CachedBalance);
        Assert.Empty(_entries);
    }

    [Fact]
    public async Task Deactivating_with_a_negative_balance_moves_nothing()
    {
        _alex.CachedBalance = -15m;

        await _service.DeactivateAsync("alex", BalanceDestination.GeneralFund, "Aged out", "Pat");

        Assert.False(_alex.IsActive);
        Assert.Equal(-15m, _alex.CachedBalance);
        Assert.Empty(_entries);
    }

    [Fact]
    public async Task Moving_the_balance_while_deactivating_still_needs_a_reason()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeactivateAsync("alex", BalanceDestination.GeneralFund, "", "Pat"));

        Assert.True(_alex.IsActive);
    }

    [Fact]
    public async Task Reactivating_is_recorded()
    {
        _alex.IsActive = false;

        await _service.ReactivateAsync("alex", "Pat");

        Assert.True(_alex.IsActive);
        await _audit.Received(1).AddAsync(FinanceAuditEntityType.Scout, "alex", "Reactivated", "Pat", Arg.Any<string?>(), Arg.Any<string?>());
    }
}
