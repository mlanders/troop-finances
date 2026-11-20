using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static BsaTroopFinances.Services.PdfStyle;

namespace BsaTroopFinances.Services;

/// <summary>One row of the "where the money is" table: its value at the start and end of the period.</summary>
public record TroopReportLine(string Name, decimal? Start, decimal? End, bool IsTotal = false, bool Indent = false)
{
    public decimal? Change => Start is { } start && End is { } end ? end - start : null;
}

/// <summary>
/// The troop finance report for the PLC and committee: the charts (shared with the web page) and
/// the PDF. Figures are troop-wide totals; invoices and reimbursements appear only grouped by
/// what they were for, never by who.
/// </summary>
public static class TroopReportPdf
{
    static TroopReportPdf() => QuestPDF.Settings.License = LicenseType.Community;

    internal const string Gold = "#E0A100";
    internal const string Green = "#1E7A34";
    internal const string Grey = "#8A96A3";

    private static readonly string[] AccountColors = [Navy, Gold, Green, "#6B4FA0", Grey, Red];

    public static string AccountColor(int index) => AccountColors[index % AccountColors.Length];

    /// <summary>The money picture from the first point to the last: each account, what's spoken
    /// for, and what's left.</summary>
    public static IReadOnlyList<TroopReportLine> Lines(TroopReport report)
    {
        var first = report.Points[0];
        var now = report.Current;
        var lines = report.ShownAccounts
            .Select(a => new TroopReportLine(a.Name, first.AccountBalances.TryGetValue(a.AccountId, out var s) ? s : null, now.AccountBalances.GetValueOrDefault(a.AccountId), Indent: true))
            .ToList();
        lines.Add(new("In the bank", first.Bank, now.Bank, IsTotal: true));
        lines.Add(new("Held for scouts", -first.Scouts, -now.Scouts, Indent: true));
        lines.Add(new("Reimbursements owed", -first.ReimbursementsOwed, -now.ReimbursementsOwed, Indent: true));
        lines.Add(new("Free to spend", first.FreeToSpend, now.FreeToSpend, IsTotal: true));
        lines.Add(new("Invoices still to be paid", first.InvoicesOutstanding, now.InvoicesOutstanding));
        return lines;
    }

    public static string TrendSvg(TroopReport report, bool interactive = false)
    {
        var points = report.Points;
        return TrendChart.Line(
            "Troop money at each month-end",
            points.Select(p => p.Date).ToList(),
            [
                new("In the bank", Navy, points.Select(p => p.Bank).ToList()),
                new("Held for scouts", Gold, points.Select(p => (decimal?)p.Scouts).ToList()),
                new("Free to spend", Green, points.Select(p => p.FreeToSpend).ToList()),
                new("Reimbursements owed", Red, points.Select(p => (decimal?)p.ReimbursementsOwed).ToList()),
                new("Invoices still to be paid", Grey, points.Select(p => (decimal?)p.InvoicesOutstanding).ToList(), Dashed: true),
            ],
            interactive);
    }

    public static string AccountsSvg(TroopReport report, bool interactive = false)
    {
        var points = report.Points;
        return TrendChart.StackedBars(
            "Bank account balances at each month-end",
            points.Select(p => p.Date).ToList(),
            report.ShownAccounts.Select((a, i) => new ChartSeries(
                a.Name, AccountColor(i), points.Select(p => p.AccountBalances.TryGetValue(a.AccountId, out var b) ? b : (decimal?)null).ToList())).ToList(),
            interactive);
    }

    public static string Period(TroopReport report) =>
        $"{report.From.ToString("MMM d, yyyy", Us)} to {report.To.ToString("MMM d, yyyy", Us)}";

    public static byte[] Create(TroopInfo troop, TroopReport report, DateTime printed)
    {
        var now = report.Current;
        return Document.Create(document => document.Page(page =>
        {
            Setup(page, $"{troop.Name} finance report · {Period(report)} · printed {printed.ToString("MMM d, yyyy", Us)}");
            page.Content().Column(column =>
            {
                column.Item().Element(c => Letterhead(c, troop, "Troop Finances", null));
                column.Item().PaddingTop(8).Text($"For the PLC and troop committee · {Period(report)}").FontColor(Muted);

                column.Item().PaddingTop(16).Row(row =>
                {
                    Figure(row, "In the bank", now.Bank, Navy);
                    Figure(row, "Held for scouts", now.Scouts, Gold);
                    Figure(row, "Reimbursements owed", now.ReimbursementsOwed, Red);
                    Figure(row, "Free to spend", now.FreeToSpend, Green);
                });

                Section(column, "Where the money is", c => LinesTable(c, report));
                Section(column, "Change over time", c => c.Svg(TrendSvg(report)));

                column.Item().PageBreak();
                if (report.ShownAccounts.Any())
                {
                    Section(column, "Bank accounts", c => c.Svg(AccountsSvg(report)));
                }

                Section(column, "Invoices still to be paid, by what they're for", c => GroupTable(c, report.InvoiceGroups, reimbursements: false));
                Section(column, "Reimbursements owed, by what they're for", c => GroupTable(c, report.ReimbursementGroups, reimbursements: true));
            });
        })).GeneratePdf();
    }

    private static void Figure(RowDescriptor row, string name, decimal? value, string color) =>
        row.RelativeItem().PaddingRight(8).BorderTop(3).BorderColor(color).Background(Colors.Grey.Lighten5).Padding(8).Column(figure =>
        {
            figure.Item().Element(Label).Text(name.ToUpperInvariant());
            figure.Item().Text(value is { } v ? Money(v) : "—").FontSize(15).Bold().FontColor(Navy);
        });

    private static void LinesTable(IContainer container, TroopReport report) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn(1.4f);
                columns.RelativeColumn(1.4f);
                columns.RelativeColumn(1.4f);
            });
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("");
                header.Cell().Element(HeaderCell).AlignRight().Text(report.From.ToString("MMM d, yyyy", Us));
                header.Cell().Element(HeaderCell).AlignRight().Text(report.To.ToString("MMM d, yyyy", Us));
                header.Cell().Element(HeaderCell).AlignRight().Text("Change");
            });
            foreach (var line in Lines(report))
            {
                var cell = line.IsTotal ? (Func<IContainer, IContainer>)(c => Cell(c).DefaultTextStyle(t => t.Bold().FontColor(Navy))) : Cell;
                table.Cell().Element(cell).PaddingLeft(line.Indent ? 14 : 4).Text(line.Name);
                table.Cell().Element(cell).AlignRight().Text(line.Start is { } s ? Money(s) : "—");
                table.Cell().Element(cell).AlignRight().Text(line.End is { } e ? Money(e) : "—");
                table.Cell().Element(cell).AlignRight().Text(line.Change is { } c ? (c > 0 ? "+" : "") + Money(c) : "—");
            }
        });

    private static void GroupTable(IContainer container, IReadOnlyList<DescriptionGroup> groups, bool reimbursements)
    {
        if (groups.Count == 0)
        {
            container.Text(reimbursements ? "None: every reimbursement has been paid." : "None: every invoice has been paid.").FontColor(Muted);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.RelativeColumn(1);
                if (reimbursements)
                {
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(1.6f);
                }
                else
                {
                    columns.RelativeColumn(1.6f);
                }

                columns.RelativeColumn(1.6f);
            });
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("For");
                header.Cell().Element(HeaderCell).AlignRight().Text(reimbursements ? "Requests" : "Invoices");
                if (reimbursements)
                {
                    header.Cell().Element(HeaderCell).AlignRight().Text("Awaiting approval");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Approved");
                }
                else
                {
                    header.Cell().Element(HeaderCell).AlignRight().Text("Oldest");
                }

                header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
            });
            foreach (var group in groups)
            {
                table.Cell().Element(Cell).Text(group.Description);
                table.Cell().Element(Cell).AlignRight().Text(group.Count.ToString(Us));
                if (reimbursements)
                {
                    table.Cell().Element(Cell).AlignRight().Text(Money(group.AwaitingApproval));
                    table.Cell().Element(Cell).AlignRight().Text(Money(group.Amount - group.AwaitingApproval));
                }
                else
                {
                    table.Cell().Element(Cell).AlignRight().Text(group.Oldest.ToString("MMM d, yyyy", Us));
                }

                table.Cell().Element(Cell).AlignRight().Text(Money(group.Amount));
            }

            table.Cell().Element(TotalCell).Text("Total");
            table.Cell().Element(TotalCell).AlignRight().Text(groups.Sum(g => g.Count).ToString(Us));
            if (reimbursements)
            {
                table.Cell().Element(TotalCell).AlignRight().Text(Money(groups.Sum(g => g.AwaitingApproval)));
                table.Cell().Element(TotalCell).AlignRight().Text(Money(groups.Sum(g => g.Amount - g.AwaitingApproval)));
            }
            else
            {
                table.Cell().Element(TotalCell).Text("");
            }

            table.Cell().Element(TotalCell).AlignRight().Text(Money(groups.Sum(g => g.Amount)));
        });
    }
}
