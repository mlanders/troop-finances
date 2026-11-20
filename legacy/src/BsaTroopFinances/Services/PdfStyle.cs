using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BsaTroopFinances.Services;

/// <summary>The look shared by the app's PDFs (InvoicePdf, BankPdf): colours, the troop's
/// letterhead, and table cells.</summary>
internal static class PdfStyle
{
    internal const string Navy = "#003F87";
    internal const string Red = "#CE1126";
    internal const string Muted = "#57677A";
    internal const string Rule = "#D9DEE5";

    internal static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    internal static string Money(decimal amount) => amount.ToString("C", Us);

    internal static string Date(DateTimeOffset? date) => date?.UtcDateTime.ToString("MMM d, yyyy", Us) ?? "—";

    /// <summary>A heading and its table, moved to the next page together rather than leaving the
    /// heading alone at the foot of a page.</summary>
    internal static void Section(ColumnDescriptor column, string heading, Action<IContainer> content) =>
        column.Item().PaddingTop(22).EnsureSpace(120).Column(section =>
        {
            section.Item().PaddingBottom(4).Text(heading).FontSize(13).Bold().FontColor(Navy);
            content(section.Item());
        });

    internal static void Setup(PageDescriptor page, string footer)
    {
        page.Size(PageSizes.Letter);
        page.Margin(0.75f, Unit.Inch);
        page.DefaultTextStyle(text => text.FontSize(10.5f).FontColor("#1F2A37"));
        page.Footer().Text(footer).FontSize(9).FontColor(Muted);
    }

    internal static void Letterhead(IContainer container, TroopInfo troop, string title, string? stamp) =>
        container.BorderBottom(2.5f).BorderColor(Navy).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(organisation =>
            {
                organisation.Item().Text(troop.Name).FontSize(20).ExtraBold().FontColor(Navy);
                organisation.Item().Text("Troop Finance").FontSize(9).FontColor(Muted);
            });
            row.RelativeItem().AlignRight().Column(heading =>
            {
                heading.Item().AlignRight().Text(title).FontSize(26).Bold().FontColor(Navy);
                if (stamp is not null)
                {
                    heading.Item().AlignRight().PaddingTop(2).Border(1.5f).BorderColor(Red).PaddingVertical(2).PaddingHorizontal(8)
                        .Text(stamp).FontSize(11).Bold().FontColor(Red).LetterSpacing(0.08f);
                }
            });
        });

    internal static void Facts(IContainer container, params (string Name, string Value)[] facts) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            foreach (var (name, value) in facts)
            {
                table.Cell().PaddingBottom(3).Text(name).SemiBold().FontColor(Muted);
                table.Cell().PaddingBottom(3).AlignRight().Text(value);
            }
        });

    internal static IContainer Label(IContainer container) =>
        container.DefaultTextStyle(text => text.FontSize(8).Bold().FontColor(Muted).LetterSpacing(0.06f)).PaddingBottom(2);

    internal static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(text => text.Bold().FontColor(Navy)).BorderBottom(1).BorderColor(Rule).PaddingVertical(6).PaddingHorizontal(4);

    internal static IContainer Cell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Rule).PaddingVertical(6).PaddingHorizontal(4);

    internal static IContainer TotalCell(IContainer container) =>
        container.DefaultTextStyle(text => text.FontSize(12).Bold().FontColor(Navy)).BorderTop(1.5f).BorderColor(Navy).PaddingVertical(7).PaddingHorizontal(4);
}
