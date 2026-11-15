using Bunit;
using BsaTroopFinances.Components.Pages.Admin;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The Bank pages: account balances, making a deposit, and the deposit record PDF.</summary>
public class BankAccountsPageTests : AdminPageTestBase
{
    private static TroopAccountEntity Account(string id, string name, bool active = true) =>
        new() { RowKey = id, Name = name, Kind = TroopAccountKind.Bank, IsActive = active, OpeningDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };

    [Fact]
    public void Accounts_show_balances_and_a_total_for_the_active_ones()
    {
        SignInAs(AdminRole.Reader);
        Fake<ITroopAccountService>().GetSummariesAsync().Returns(new List<BankAccountSummary>
        {
            new(Account("chk", "Troop checking"), 2613.60m, 2500m, 3, null),
            new(Account("ven", "Venmo"), 0m, 0m, 2, new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)),
            new(Account("old", "Old savings", active: false), 0m, 0m, 0, null),
        });

        var page = Render<BankAccounts>();

        var rows = page.FindAll("tbody tr");
        Assert.Equal(4, rows.Count);
        Assert.Contains("$2,613.60", rows[0].TextContent);
        Assert.Contains("Sep 30, 2026", rows[1].TextContent);
        Assert.Contains("Inactive", rows[2].TextContent);
        Assert.Contains("$2,613.60", rows[3].TextContent);           // two active accounts, one total
        Assert.Empty(page.FindAll(".action-bar"));                     // a viewer can't add anything
    }
}

public class BankDepositsPageTests : AdminPageTestBase
{
    private readonly ITroopAccountService _service;

    public BankDepositsPageTests()
    {
        SignInAs(AdminRole.Writer);
        var bank = Fake<ITroopBankRepository>();
        bank.GetAccountsAsync().Returns(new List<TroopAccountEntity> { new() { RowKey = "chk", Name = "Troop checking", Kind = TroopAccountKind.Bank, IsActive = true } });
        bank.GetDepositsAsync().Returns(new List<DepositEntity>());
        Fake<IFundraiserEventRepository>().GetAllAsync().Returns(new List<FundraiserEventEntity>());
        _service = Fake<ITroopAccountService>();
        _service.GetUndepositedAsync().Returns(new List<UndepositedItem>
        {
            new(DepositLineKind.InvoicePayment, "p1", new(2026, 10, 1, 17, 0, 0, TimeSpan.Zero), "Sam Patel", "Troop T-shirt", InvoicePaymentMethod.Check, "1042", 60),
            new(DepositLineKind.Donation, "d1", new(2026, 10, 2, 17, 0, 0, TimeSpan.Zero), "Main Street Hardware", "Donation", InvoicePaymentMethod.Cash, "", 35),
        });
    }

    [Fact]
    public void Cash_on_the_deposit_must_be_counted_before_it_can_be_made()
    {
        var page = Render<BankDeposits>();
        page.FindAll("tbody input[type=checkbox]")[0].Change(true);
        page.FindAll("tbody input[type=checkbox]")[1].Change(true);

        Assert.Contains("$95.00", page.Find("#depositTotal").TextContent);
        Assert.Contains("$35.00 short", page.Find("#cashCheck").TextContent);
        Assert.True(page.Find("#makeDeposit").HasAttribute("disabled"));

        page.Find("input[aria-label='Number of $20']").Change("1");
        page.Find("input[aria-label='Number of $10']").Change("1");
        page.Find("input[aria-label='Number of $5']").Change("1");

        Assert.DoesNotContain("short", page.Find("#cashCheck").TextContent);
        Assert.False(page.Find("#makeDeposit").HasAttribute("disabled"));
    }

    [Fact]
    public void Making_the_deposit_sends_the_ticked_items_and_the_count()
    {
        _service.CreateDepositAsync(Arg.Any<DepositRequest>(), Arg.Any<string>()).Returns(new DepositEntity { RowKey = "dep1" });
        var page = Render<BankDeposits>();
        page.FindAll("tbody input[type=checkbox]")[0].Change(true);       // just the check: no cash to count

        page.Find("#makeDeposit").Click();

        var request = (DepositRequest)_service.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITroopAccountService.CreateDepositAsync)).GetArguments()[0]!;
        Assert.Equal("chk", request.AccountId);
        Assert.Equal([(DepositLineKind.InvoicePayment, "p1")], request.Items);
        Assert.Empty(request.CashCount);
    }

    [Fact]
    public void A_viewer_sees_whats_waiting_but_cant_deposit_it()
    {
        SignInAs(AdminRole.Reader);

        var page = Render<BankDeposits>();

        Assert.Equal(2, page.FindAll("tbody tr").Count);
        Assert.Empty(page.FindAll("#newDeposit"));
        Assert.Empty(page.FindAll("tbody input[type=checkbox]"));
    }
}

public class BankReconcilePageTests : AdminPageTestBase
{
    private readonly ITroopAccountService _service;
    private readonly ReconciliationEntity _reconciliation = new()
    {
        PartitionKey = "chk", RowKey = "rec1", StatementDate = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), StatementBalance = 2495,
        Status = ReconciliationStatus.InProgress,
    };
    private readonly List<TroopTransactionEntity> _entries =
    [
        new() { PartitionKey = "chk", RowKey = "fee", Date = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), Amount = -5, Kind = TroopTransactionKind.BankFee, Description = "Monthly fee" },
        new() { PartitionKey = "chk", RowKey = "chk301", Date = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), Amount = -120, Kind = TroopTransactionKind.Expense, Payee = "Scout Shop", Reference = "301" },
    ];

    public BankReconcilePageTests()
    {
        var bank = Fake<ITroopBankRepository>();
        bank.GetAccountsAsync().Returns(new List<TroopAccountEntity> { new() { RowKey = "chk", Name = "Troop checking", Kind = TroopAccountKind.Bank, IsActive = true } });
        bank.GetReconciliationsAsync("chk").Returns(new List<ReconciliationEntity> { _reconciliation });
        _service = Fake<ITroopAccountService>();
    }

    private void ViewWith(decimal ticked) =>
        _service.GetReconciliationAsync("chk", "rec1").Returns(new ReconciliationView(_reconciliation, _entries, 2500, ticked));

    [Fact]
    public void Finish_waits_until_the_difference_is_zero()
    {
        SignInAs(AdminRole.Writer);
        ViewWith(0);

        var page = Render<BankReconcile>();

        Assert.Contains("$5.00", page.Find("#difference").TextContent);
        Assert.Contains("money-negative", page.Find("#difference").ClassName);
        Assert.True(page.Find("#finish").HasAttribute("disabled"));
        Assert.Equal(2, page.FindAll("#reconciliation tbody tr").Count);

        ViewWith(-5);
        page.FindAll("#reconciliation tbody input[type=checkbox]")[0].Change(true);

        _service.Received(1).SetClearedAsync("chk", "rec1", Arg.Is<IReadOnlySet<string>>(s => s.SetEquals(new[] { "fee" })));
        Assert.Equal("$0.00", page.Find("#difference").TextContent);
        Assert.False(page.Find("#finish").HasAttribute("disabled"));
    }

    [Fact]
    public void A_viewer_can_see_the_reconciliation_but_not_tick_or_finish_it()
    {
        SignInAs(AdminRole.Reader);
        ViewWith(0);

        var page = Render<BankReconcile>();

        Assert.All(page.FindAll("#reconciliation tbody input[type=checkbox]"), box => Assert.True(box.HasAttribute("disabled")));
        Assert.Empty(page.FindAll("#finish"));
    }
}

public class BankPdfTests
{
    [Fact]
    public void A_deposit_record_lists_checks_and_the_cash_count_on_one_page()
    {
        var deposit = new DepositEntity
        {
            RowKey = "dep1", Date = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), Total = 175, CheckTotal = 60, CashTotal = 115,
            CashCount = """{"20":5,"10":1,"5":1}""", CountedBy1 = "Pat Treasurer", CountedBy2 = "Sam Leader", CreatedBy = "Pat Treasurer",
        };
        var lines = new List<DepositLineEntity>
        {
            new() { Payer = "Sam Patel", Description = "Troop T-shirt", Method = InvoicePaymentMethod.Check, Reference = "1042", Amount = 60 },
            new() { Payer = "", Description = "Car wash", Method = InvoicePaymentMethod.Cash, Amount = 115 },
        };

        var pdf = BankPdf.DepositRecord(TestTroop.Info, deposit, lines, "Troop checking", new DateTime(2026, 10, 3));

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
#pragma warning disable CA1416 // PDFium (used here only to read the result back) ships for the platforms the tests run on.
        Assert.Equal(1, PDFtoImage.Conversion.GetPageCount(pdf));
#pragma warning restore CA1416
        Assert.Equal(new Dictionary<decimal, int> { [20m] = 5, [10m] = 1, [5m] = 1 }, BankPdf.ReadCashCount(deposit.CashCount));
    }

    [Theory]
    [InlineData(100, "$100")]
    [InlineData(1, "$1")]
    [InlineData(0.25, "25¢")]
    [InlineData(0.01, "1¢")]
    public void Denominations_read_as_dollars_or_cents(decimal denomination, string label)
    {
        Assert.Equal(label, BankPdf.DenominationLabel(denomination));
    }
}
