using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Models;

/// <summary>
/// What an admin list page (Invoices, Reimbursements) is filtered by: status, a date range, a
/// scout-or-guardian name and a description. Text is kept trimmed (blank = no filter) so two
/// filters that show the same rows compare equal -- that's how the page tells a saved view apart
/// from "Custom".
/// </summary>
public sealed record ListFilters
{
    public ListFilters(string? status, DateOnly? from = null, DateOnly? to = null, string? name = null, string? description = null)
    {
        Status = status;
        From = from;
        To = to;
        Name = Clean(name);
        Description = Clean(description);
    }

    /// <summary>null = All.</summary>
    public string? Status { get; init; }

    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public string? Name { get; init => field = Clean(value); }
    public string? Description { get; init => field = Clean(value); }

    /// <summary>How many of the filters tucked behind the Filters button are on (a date range
    /// counts once).</summary>
    public int PanelCount => (From is not null || To is not null ? 1 : 0) + (Name is not null ? 1 : 0) + (Description is not null ? 1 : 0);

    /// <summary>The start of the From day. No explicit offset: picked dates are local (Pacific)
    /// days, matching how the lists display dates.</summary>
    public DateTimeOffset? FromTime => From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue)) : null;

    /// <summary>The very end of the To day, so the whole day is included.</summary>
    public DateTimeOffset? ToTime => To is { } to ? new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue).AddDays(1).AddTicks(-1)) : null;

    /// <summary>The same status, with the date range and text filters cleared.</summary>
    public ListFilters WithoutPanelFilters() => new(Status);

    public static ListFilters FromView(ListViewEntity view) =>
        new(view.Status, view.FromDate, view.ToDate, view.NameSearch, view.DescriptionSearch);

    /// <summary>A short summary for Activity History, e.g. "Status Paid; Jun 1, 2026 - Aug 31, 2026;
    /// description "summer camp"".</summary>
    public string Describe()
    {
        var parts = new List<string> { $"Status {Status ?? "All"}" };
        if (From is not null || To is not null)
        {
            parts.Add($"{From?.ToString("MMM d, yyyy") ?? "any date"} - {To?.ToString("MMM d, yyyy") ?? "any date"}");
        }
        if (Name is not null)
        {
            parts.Add($"scout or guardian \"{Name}\"");
        }
        if (Description is not null)
        {
            parts.Add($"description \"{Description}\"");
        }
        return string.Join("; ", parts);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
