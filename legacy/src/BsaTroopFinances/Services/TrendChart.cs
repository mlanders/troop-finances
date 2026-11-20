using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BsaTroopFinances.Services;

/// <summary>One line or bar layer on a chart: a value per date, null where there's no data yet.</summary>
public record ChartSeries(string Name, string Color, IReadOnlyList<decimal?> Values, bool Dashed = false);

/// <summary>
/// Draws the troop report's charts as SVG text, so the web page (inline) and the PDF (QuestPDF's
/// Svg element) show the same picture without a charting package. Text uses Lato, the font
/// QuestPDF carries with it, so labels render on a server with no fonts of its own.
/// An interactive chart (the web page's) also carries a hover column per date with that date's
/// values, which chart-tooltip.js shows beside the pointer; the PDF leaves them out.
/// </summary>
public static class TrendChart
{
    private const int Width = 720;
    private const int Height = 300;
    private const int Left = 64;
    private const int Right = 16;
    private const int Top = 44;
    private const int Bottom = 40;
    private const string Font = "Lato, Helvetica, Arial, sans-serif";
    private const string Grid = "#D9DEE5";
    private const string Ink = "#57677A";

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A line per series across the dates.</summary>
    public static string Line(string title, IReadOnlyList<DateOnly> dates, IReadOnlyList<ChartSeries> series, bool interactive = false)
    {
        var values = series.SelectMany(s => s.Values).OfType<decimal>().ToList();
        var (low, high, step) = Scale(values.DefaultIfEmpty(0).Min(), values.DefaultIfEmpty(0).Max());
        var svg = Begin(title, dates, series, low, high, step);
        var x = Positions(dates.Count);

        foreach (var line in series)
        {
            // A break in the data (null) ends one run of the line and starts the next.
            var run = new List<string>();
            void Flush()
            {
                if (run.Count > 1)
                {
                    svg.Append($"<polyline fill=\"none\" stroke=\"{line.Color}\" stroke-width=\"2.5\" stroke-linejoin=\"round\" stroke-linecap=\"round\"{(line.Dashed ? " stroke-dasharray=\"6 4\"" : "")} points=\"{string.Join(' ', run)}\" />");
                }

                run.Clear();
            }

            for (var i = 0; i < dates.Count; i++)
            {
                if (line.Values[i] is { } value)
                {
                    var y = Y(value, low, high);
                    run.Add($"{N(x[i])},{N(y)}");
                    svg.Append($"<circle cx=\"{N(x[i])}\" cy=\"{N(y)}\" r=\"3\" fill=\"{line.Color}\" />");
                }
                else
                {
                    Flush();
                }
            }

            Flush();
        }

        if (interactive)
        {
            HoverColumns(svg, dates, series, x, total: false, (i, guide) =>
            {
                guide.Append($"<line x1=\"{N(x[i])}\" y1=\"{Top}\" x2=\"{N(x[i])}\" y2=\"{Height - Bottom}\" stroke=\"{Ink}\" stroke-width=\"1\" stroke-dasharray=\"3 3\" />");
                foreach (var line in series.Where(s => s.Values[i] is not null))
                {
                    guide.Append($"<circle cx=\"{N(x[i])}\" cy=\"{N(Y(line.Values[i]!.Value, low, high))}\" r=\"5.5\" fill=\"{line.Color}\" stroke=\"#fff\" stroke-width=\"2\" />");
                }
            });
        }

        return svg.Append("</svg>").ToString();
    }

    /// <summary>A bar per date with each series stacked on the last; negative values stack below zero.</summary>
    public static string StackedBars(string title, IReadOnlyList<DateOnly> dates, IReadOnlyList<ChartSeries> series, bool interactive = false)
    {
        var ups = Enumerable.Range(0, dates.Count).Select(i => series.Sum(s => Math.Max(s.Values[i] ?? 0, 0))).ToList();
        var downs = Enumerable.Range(0, dates.Count).Select(i => series.Sum(s => Math.Min(s.Values[i] ?? 0, 0))).ToList();
        var (low, high, step) = Scale(downs.DefaultIfEmpty(0).Min(), ups.DefaultIfEmpty(0).Max());
        var svg = Begin(title, dates, series, low, high, step);
        var x = Positions(dates.Count);
        var barWidth = Math.Min(36.0, (Width - Left - Right) / (double)Math.Max(dates.Count, 1) * 0.6);

        for (var i = 0; i < dates.Count; i++)
        {
            decimal up = 0, down = 0;
            foreach (var layer in series)
            {
                var value = layer.Values[i] ?? 0;
                if (value == 0)
                {
                    continue;
                }

                var (from, to) = value > 0 ? (up, up += value) : (down, down += value);
                var top = Y(Math.Max(from, to), low, high);
                var bottom = Y(Math.Min(from, to), low, high);
                svg.Append($"<rect x=\"{N(x[i] - barWidth / 2)}\" y=\"{N(top)}\" width=\"{N(barWidth)}\" height=\"{N(Math.Max(bottom - top, 0.5))}\" fill=\"{layer.Color}\" />");
            }
        }

        if (interactive)
        {
            var slot = (Width - Left - Right) / (double)Math.Max(dates.Count, 1);
            HoverColumns(svg, dates, series, x, total: series.Count > 1, (i, guide) =>
                guide.Append($"<rect x=\"{N(x[i] - slot / 2)}\" y=\"{Top}\" width=\"{N(slot)}\" height=\"{Height - Top - Bottom}\" fill=\"{Ink}\" fill-opacity=\"0.1\" />"));
        }

        return svg.Append("</svg>").ToString();
    }

    /// <summary>
    /// A column per date covering the plot's full height: a see-through hit area and a guide that
    /// CSS shows while the column is hovered or focused. The date's values ride along as JSON for
    /// the tooltip, and as an aria-label for screen readers.
    /// </summary>
    private static void HoverColumns(StringBuilder svg, IReadOnlyList<DateOnly> dates, IReadOnlyList<ChartSeries> series, double[] x, bool total, Action<int, StringBuilder> drawGuide)
    {
        var slot = (Width - Left - Right) / (double)Math.Max(dates.Count, 1);
        for (var i = 0; i < dates.Count; i++)
        {
            var label = dates[i].ToString("MMM d, yyyy", Us);
            var rows = series.Select(s => new TooltipRow(s.Name, s.Color, Money(s.Values[i]), s.Dashed)).ToList();
            if (total)
            {
                rows.Add(new TooltipRow("Total", "", Money(series.Any(s => s.Values[i] is not null) ? series.Sum(s => s.Values[i] ?? 0) : null), false));
            }

            var guide = new StringBuilder();
            drawGuide(i, guide);
            var spoken = $"{label}: {string.Join(", ", rows.Select(r => $"{r.Name} {r.Value}"))}";
            svg.Append($"<g class=\"chart-column\" tabindex=\"0\" role=\"img\" aria-label=\"{Text(spoken)}\" data-label=\"{Text(label)}\" data-values=\"{Text(JsonSerializer.Serialize(rows, JsonOptions))}\">");
            svg.Append($"<g class=\"chart-guide\">{guide}</g>");
            svg.Append($"<rect class=\"chart-hit\" x=\"{N(x[i] - slot / 2)}\" y=\"{Top}\" width=\"{N(slot)}\" height=\"{Height - Top - Bottom}\" fill=\"transparent\" />");
            svg.Append("</g>");
        }
    }

    private sealed record TooltipRow(string Name, string Color, string Value, bool Dashed);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string Money(decimal? value) => value?.ToString("C", Us) ?? "—";

    /// <summary>The frame shared by both charts: legend, gridlines with dollar labels, and dates.</summary>
    private static StringBuilder Begin(string title, IReadOnlyList<DateOnly> dates, IReadOnlyList<ChartSeries> series, decimal low, decimal high, decimal step)
    {
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\" role=\"img\" aria-label=\"{Text(title)}\" font-family=\"{Font}\">");
        svg.Append($"<title>{Text(title)}</title>");

        // Legend across the top, wrapping onto a second row if it runs out of room.
        double legendX = Left, legendY = 12;
        foreach (var item in series)
        {
            var itemWidth = 26 + item.Name.Length * 6.6 + 18;
            if (legendX + itemWidth > Width - Right && legendX > Left)
            {
                legendX = Left;
                legendY += 16;
            }

            svg.Append(item.Dashed
                ? $"<line x1=\"{N(legendX)}\" y1=\"{N(legendY)}\" x2=\"{N(legendX + 18)}\" y2=\"{N(legendY)}\" stroke=\"{item.Color}\" stroke-width=\"2.5\" stroke-dasharray=\"5 3\" />"
                : $"<rect x=\"{N(legendX)}\" y=\"{N(legendY - 5)}\" width=\"18\" height=\"10\" rx=\"2\" fill=\"{item.Color}\" />");
            svg.Append($"<text x=\"{N(legendX + 24)}\" y=\"{N(legendY + 4)}\" font-size=\"12\" fill=\"#1F2A37\">{Text(item.Name)}</text>");
            legendX += itemWidth;
        }

        for (var value = low; value <= high; value += step)
        {
            var y = Y(value, low, high);
            svg.Append($"<line x1=\"{Left}\" y1=\"{N(y)}\" x2=\"{Width - Right}\" y2=\"{N(y)}\" stroke=\"{(value == 0 ? Ink : Grid)}\" stroke-width=\"1\" />");
            svg.Append($"<text x=\"{Left - 8}\" y=\"{N(y + 4)}\" font-size=\"11\" fill=\"{Ink}\" text-anchor=\"end\">{Text(Dollars(value))}</text>");
        }

        // Every date is labelled while they fit; past 14, every other one (always the last).
        var x = Positions(dates.Count);
        var every = dates.Count > 14 ? 2 : 1;
        for (var i = 0; i < dates.Count; i++)
        {
            if (i % every != 0 && i != dates.Count - 1)
            {
                continue;
            }

            var date = dates[i];
            var isMonthEnd = date.AddDays(1).Day == 1;
            var label = isMonthEnd ? date.ToString("MMM", Us) : date.ToString("MMM d", Us);
            svg.Append($"<text x=\"{N(x[i])}\" y=\"{Height - Bottom + 16}\" font-size=\"11\" fill=\"{Ink}\" text-anchor=\"middle\">{Text(label)}</text>");
            if (i == 0 || date.Month == 1 && isMonthEnd || i == dates.Count - 1)
            {
                svg.Append($"<text x=\"{N(x[i])}\" y=\"{Height - Bottom + 30}\" font-size=\"10\" fill=\"{Ink}\" text-anchor=\"middle\">{date.Year}</text>");
            }
        }

        return svg;
    }

    /// <summary>The axis from a round number at or below the lowest value (never above zero) to one
    /// at or above the highest, in four or five even steps.</summary>
    public static (decimal Low, decimal High, decimal Step) Scale(decimal min, decimal max)
    {
        min = Math.Min(min, 0);
        max = Math.Max(max, 0);
        if (max == min)
        {
            max = min + 100;
        }

        var rough = (double)(max - min) / 4;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = (decimal)(new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= rough));
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }

    private static double[] Positions(int count)
    {
        var plot = Width - Left - Right;
        var slot = plot / (double)Math.Max(count, 1);
        return Enumerable.Range(0, count).Select(i => Left + slot * (i + 0.5)).ToArray();
    }

    private static double Y(decimal value, decimal low, decimal high) =>
        Top + (Height - Top - Bottom) * (double)((high - value) / (high - low));

    public static string Dollars(decimal value)
    {
        var size = Math.Abs(value);
        var text = size >= 1000 ? $"${(size / 1000).ToString("0.#", Us)}k" : $"${size.ToString("0", Us)}";
        return value < 0 ? $"-{text}" : text;
    }

    private static string N(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Text(string value) => WebUtility.HtmlEncode(value);
}
