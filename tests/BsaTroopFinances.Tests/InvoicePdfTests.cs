using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using SkiaSharp;

namespace BsaTroopFinances.Tests;

/// <summary>The invoice PDFs are real PDF files with the right number of pages and something drawn on them.</summary>
public class InvoicePdfTests
{
    private static readonly DateTime Printed = new(2026, 10, 8);

    private static InvoiceEntity Invoice(int n = 1, string status = InvoiceStatus.Outstanding) => new()
    {
        PartitionKey = "alex", RowKey = $"inv{n}", InvoiceId = $"inv{n}", ScoutId = "alex", ScoutName = "Alex Rivera",
        Description = $"Summer camp deposit {n}", TotalAmount = 275, AmountAppliedFromAccount = 100, AmountDueFromFamily = 125, StatusValue = status,
        IssuedUtc = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero), DueDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
    };

#pragma warning disable CA1416 // PDFium (used here only to read the result back) ships for the platforms the tests run on.
    private static int Pages(byte[] pdf) => PDFtoImage.Conversion.GetPageCount(pdf);

    /// <summary>True when the page has the letterhead's navy on it, i.e. it isn't a blank sheet.</summary>
    private static bool HasNavyInk(byte[] pdf, int page)
    {
        using var bitmap = PDFtoImage.Conversion.ToImage(pdf, page: page);
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red < 40 && c.Green is > 40 and < 90 && c.Blue is > 110 and < 160)
                {
                    return true;
                }
            }
        }

        return false;
    }
#pragma warning restore CA1416

    private static void AssertIsPdf(byte[] pdf) => Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));

    [Theory]
    [InlineData(InvoiceStatus.Outstanding)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Voided)]
    public void A_single_invoice_is_a_one_page_pdf(string status)
    {
        var payments = new List<InvoicePaymentEntity>
        {
            new() { Amount = 50, Method = InvoicePaymentMethod.Venmo, Reference = "@rivera-family", PaidUtc = new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero) },
        };

        var pdf = InvoicePdf.Invoice(TestTroop.Info, new InvoiceSheet(Invoice(status: status), payments, ["Maria Rivera", "Luis Rivera"]), Printed);

        AssertIsPdf(pdf);
        Assert.Equal(1, Pages(pdf));
        Assert.True(HasNavyInk(pdf, 0));
    }

    [Fact]
    public void An_adult_invoice_with_no_payments_or_guardians_still_draws()
    {
        var adult = new InvoiceEntity
        {
            PartitionKey = "maria", RowKey = "inv9", InvoiceId = "inv9", GuardianId = "maria", GuardianName = "Maria Rivera", Description = "Summer camp, adult",
            TotalAmount = 320, AmountDueFromFamily = 320, StatusValue = InvoiceStatus.Outstanding, IssuedUtc = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero),
        };

        var pdf = InvoicePdf.Invoice(TestTroop.Info, new InvoiceSheet(adult, [], []), Printed);

        AssertIsPdf(pdf);
        Assert.Equal(1, Pages(pdf));
    }

    [Fact]
    public void Each_family_starts_on_its_own_page()
    {
        var families = new[] { "Chris Lee", "Family of Sam Patel", "Luis Rivera and Maria Rivera" }
            .Select(name => new FamilySheet(name, [Invoice(1), Invoice(2)])).ToList();

        var pdf = InvoicePdf.Families(TestTroop.Info, families, Printed);

        AssertIsPdf(pdf);
        Assert.Equal(3, Pages(pdf));
        Assert.All(Enumerable.Range(0, 3), page => Assert.True(HasNavyInk(pdf, page)));
    }

    [Fact]
    public void A_family_invoice_with_scout_account_money_still_fits_one_page()
    {
        var invoice = Invoice(1);
        var family = new FamilySheet("Maria Rivera", [invoice])
        {
            ScoutFunds = [new ScoutFunds(invoice.ScoutId, invoice.ScoutName, 40m)],
        };

        var pdf = InvoicePdf.Families(TestTroop.Info, [family], Printed);

        AssertIsPdf(pdf);
        Assert.Equal(1, Pages(pdf));
    }

    [Fact]
    public void A_family_with_more_invoices_than_fit_runs_onto_further_pages()
    {
        var family = new FamilySheet("Maria Rivera", Enumerable.Range(1, 60).Select(n => Invoice(n)).ToList());

        Assert.InRange(Pages(InvoicePdf.Families(TestTroop.Info, [family], Printed)), 2, 4);
    }

    private static FamilyStatusSheet StatusSheet(string name, int entries = 3)
    {
        var alex = new ScoutEntity { RowKey = "alex", FullName = "Alex Rivera", IsActive = true };
        var casey = new ScoutEntity { RowKey = "casey", FullName = "Casey Rivera", IsActive = false };
        var ledger = Enumerable.Range(1, entries).Select(n => new ScoutLedgerEntryEntity
        {
            RowKey = $"{n:D19}", CreatedUtc = new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero).AddDays(n), Description = $"Popcorn sale {n}",
            EntryTypeValue = n % 3 == 0 ? LedgerEntryType.Debit : LedgerEntryType.Credit, Amount = 10 + n,
        }).ToList();
        var payment = new InvoicePaymentEntity { Amount = 150, Method = InvoicePaymentMethod.Check, Reference = "1042", PaidUtc = new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero) };

        return new FamilyStatusSheet(
            new Household(name, [new GuardianEntity { GuardianId = "maria", FullName = name }], ["alex", "casey"]),
            new DateOnly(2025, 10, 8), new DateOnly(2026, 10, 8),
            [new ScoutFundStatus(alex, 40, ledger, 125), new ScoutFundStatus(casey, 25, [], 0)],
            320, [Invoice(1), Invoice(2, InvoiceStatus.Paid)], [new StatusPaymentLine(payment, "Alex Rivera — Summer camp deposit 2")]);
    }

    [Fact]
    public void Each_familys_status_sheet_starts_on_its_own_page()
    {
        var one = Pages(InvoicePdf.FamilyStatus(TestTroop.Info, [StatusSheet("Chris Lee")], Printed));
        var pdf = InvoicePdf.FamilyStatus(TestTroop.Info, [StatusSheet("Luis Rivera and Maria Rivera"), StatusSheet("Chris Lee")], Printed);

        AssertIsPdf(pdf);
        Assert.Equal(2 * one, Pages(pdf));
        Assert.True(HasNavyInk(pdf, one));          // the second family's letterhead
    }

    [Fact]
    public void A_long_scout_fund_runs_onto_further_pages()
    {
        Assert.InRange(Pages(InvoicePdf.FamilyStatus(TestTroop.Info, [StatusSheet("Maria Rivera", entries: 80)], Printed)), 2, 5);
    }

    [Fact]
    public void An_adult_with_no_scouts_and_nothing_in_the_period_still_gets_a_sheet()
    {
        var sheet = new FamilyStatusSheet(
            new Household("Lee Leader", [new GuardianEntity { GuardianId = "lee", FullName = "Lee Leader" }], []),
            new DateOnly(2025, 10, 8), new DateOnly(2026, 10, 8), [], 0, [], []);

        var pdf = InvoicePdf.FamilyStatus(TestTroop.Info, [sheet], Printed);

        AssertIsPdf(pdf);
        Assert.Equal(1, Pages(pdf));
    }
}
