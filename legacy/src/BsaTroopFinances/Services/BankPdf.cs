using System.Globalization;
using System.Text.Json;
using BsaTroopFinances.Data.Entities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static BsaTroopFinances.Services.PdfStyle;

namespace BsaTroopFinances.Services;

/// <summary>
/// The troop's banking paperwork as PDF files: a deposit record listing every check, the cash
/// count by denomination and the two people who counted it, to file with the bank receipt.
/// </summary>
public static class BankPdf
{
    static BankPdf() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>The denominations on the cash count, largest first.</summary>
    public static readonly IReadOnlyList<decimal> Denominations = [100m, 50m, 20m, 10m, 5m, 2m, 1m, 0.50m, 0.25m, 0.10m, 0.05m, 0.01m];

    /// <summary>Reads a deposit's stored cash count (see DepositEntity.CashCount).</summary>
    public static IReadOnlyDictionary<decimal, int> ReadCashCount(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<decimal, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(json)!
                .ToDictionary(c => decimal.Parse(c.Key, CultureInfo.InvariantCulture), c => c.Value);

    public static string DenominationLabel(decimal denomination) =>
        denomination >= 1 ? $"${denomination:0}" : $"{denomination * 100:0}¢";

    public static byte[] DepositRecord(TroopInfo troop, DepositEntity deposit, IReadOnlyList<DepositLineEntity> lines, string accountName, DateTime printed)
    {
        var checks = lines.Where(l => l.Method == InvoicePaymentMethod.Check).ToList();
        var cash = lines.Where(l => l.Method == InvoicePaymentMethod.Cash).ToList();
        var other = lines.Except(checks).Except(cash).ToList();
        var count = ReadCashCount(deposit.CashCount);

        return Document.Create(document => document.Page(page =>
        {
            Setup(page, $"Printed {printed.ToString("MMM d, yyyy", Us)}");
            page.Content().Column(column =>
            {
                column.Item().Element(c => Letterhead(c, troop, "Deposit Record", null));

                column.Item().PaddingVertical(16).Row(row =>
                {
                    row.RelativeItem().Column(info =>
                    {
                        info.Item().Element(Label).Text("Deposited to");
                        info.Item().Text(accountName);
                        info.Item().PaddingTop(10).Element(Label).Text("Recorded by");
                        info.Item().Text(deposit.CreatedBy);
                    });
                    row.ConstantItem(200).Element(c => Facts(c,
                        ("Date", Date(deposit.Date)),
                        ("Items", lines.Count.ToString(Us)),
                        ("Total", Money(deposit.Total))));
                });

                if (checks.Count > 0)
                {
                    Section(column, "Checks", section => ItemTable(section, checks, "Check no.", "Total checks"));
                }
                if (cash.Count > 0)
                {
                    Section(column, "Cash", section => ItemTable(section, cash, "Note", "Total cash"));
                    Section(column, "Cash count", section => section.Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(100);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Denomination");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Count");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                        });
                        foreach (var denomination in Denominations.Where(d => count.GetValueOrDefault(d) > 0))
                        {
                            table.Cell().Element(Cell).Text(DenominationLabel(denomination));
                            table.Cell().Element(Cell).AlignRight().Text(count[denomination].ToString(Us));
                            table.Cell().Element(Cell).AlignRight().Text(Money(denomination * count[denomination]));
                        }

                        table.Cell().ColumnSpan(2).Element(TotalCell).Text("Counted");
                        table.Cell().Element(TotalCell).AlignRight().Text(Money(count.Sum(c => c.Key * c.Value)));
                    }));
                }
                if (other.Count > 0)
                {
                    Section(column, "Other", section => ItemTable(section, other, "Reference", "Total other"));
                }

                column.Item().PaddingTop(36).Row(row =>
                {
                    foreach (var (name, index) in new[] { deposit.CountedBy1, deposit.CountedBy2 }.Select((n, i) => (n, i)))
                    {
                        if (index > 0)
                        {
                            row.ConstantItem(30);
                        }
                        row.RelativeItem().Column(signature =>
                        {
                            signature.Item().Element(Label).Text(index == 0 ? "Counted by" : "And by");
                            signature.Item().Text(string.IsNullOrWhiteSpace(name) ? " " : name);
                            signature.Item().PaddingTop(28).BorderTop(1).BorderColor(Muted).PaddingTop(3).Text("Signature and date").FontSize(8).FontColor(Muted);
                        });
                    }
                });
            });
        })).GeneratePdf();
    }

    private static void ItemTable(IContainer container, IReadOnlyList<DepositLineEntity> lines, string referenceHeading, string totalLabel) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn(4);
                columns.ConstantColumn(85);
                columns.ConstantColumn(85);
            });
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("From");
                header.Cell().Element(HeaderCell).Text("For");
                header.Cell().Element(HeaderCell).Text(referenceHeading);
                header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
            });
            foreach (var line in lines)
            {
                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(line.Payer) ? "—" : line.Payer);
                table.Cell().Element(Cell).Text(line.Description);
                table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(line.Reference) ? "—" : line.Reference);
                table.Cell().Element(Cell).AlignRight().Text(Money(line.Amount));
            }

            table.Cell().ColumnSpan(3).Element(TotalCell).Text(totalLabel);
            table.Cell().Element(TotalCell).AlignRight().Text(Money(lines.Sum(l => l.Amount)));
        });
}
