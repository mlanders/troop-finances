namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// A named set of filters for an admin list page ("Summer camp 2026" = description "summer camp",
/// issued Jun 1 - Aug 31), saved by one admin for every admin to pick. PartitionKey is the list
/// (<see cref="Invoices"/> or <see cref="Reimbursements"/>), RowKey the view's id. A view only
/// changes what's shown -- never any records -- so any admin may save one.
/// </summary>
public class ListViewEntity
{
    public const string Invoices = "invoices";
    public const string Reimbursements = "reimbursements";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string List => PartitionKey;
    public string ViewId => RowKey;

    public string Name { get; set; } = string.Empty;

    /// <summary>The status filter; null = All.</summary>
    public string? Status { get; set; }

    /// <summary>Fixed dates (the issued/submitted range), inclusive.</summary>
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public string? NameSearch { get; set; }
    public string? DescriptionSearch { get; set; }

    /// <summary>The admin's sign-in name, as Activity History records it. They (or any Editor)
    /// can delete the view.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
