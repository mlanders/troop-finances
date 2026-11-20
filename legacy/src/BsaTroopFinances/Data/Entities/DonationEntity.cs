namespace BsaTroopFinances.Data.Entities;

/// <summary>How a gift arrived: the same ways a family pays an invoice, plus donated goods.</summary>
public static class DonationMethod
{
    public const string InKind = "Goods (in-kind)";

    /// <summary>In the order offered on the donation form.</summary>
    public static readonly IReadOnlyList<string> All = [.. InvoicePaymentMethod.External, InKind];

    public static bool IsValid(string method) => All.Contains(method);

    /// <summary>"Other" must say how it was given, and goods must say what was given.</summary>
    public static bool RequiresReference(string method) => method is InvoicePaymentMethod.Other or InKind;

    public static string ReferenceLabel(string method) => method == InKind
        ? "What was given"
        : InvoicePaymentMethod.ReferenceLabel(method);

    public static string ReferencePlaceholder(string method) => method == InKind
        ? "e.g. Two 4-person tents"
        : InvoicePaymentMethod.ReferencePlaceholder(method);
}

/// <summary>
/// A gift to the troop: money or goods, from a family, business or anyone else. Part or all of a
/// money gift can be credited to scouts' accounts, through ScoutLedgerEntry rows with
/// SourceType Donation and SourceId = this donation's id (the same way fundraiser proceeds are
/// allocated). Whatever isn't credited stays with the troop, which this app doesn't keep a
/// balance for.
/// </summary>
public class DonationEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string DonorName { get; set; } = string.Empty;
    public string DonorEmail { get; set; } = string.Empty;

    /// <summary>Mailing address, for a thank-you letter.</summary>
    public string DonorAddress { get; set; } = string.Empty;

    /// <summary>The donor asked not to be named: families see "Anonymous donation" on their
    /// scout's account. Leaders still see the name.</summary>
    public bool IsAnonymous { get; set; }

    // A calendar date stored as midnight UTC, like FundraiserEventEntity.EventDate.
    public DateTimeOffset ReceivedDate { get; set; }

    /// <summary>The amount given; for goods, their estimated value.</summary>
    public decimal Amount { get; set; }

    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    /// <summary>The donor said what the gift is for (Purpose says what); otherwise it's for the
    /// troop's general use.</summary>
    public bool IsRestricted { get; set; }
    public string Purpose { get; set; } = string.Empty;

    /// <summary>When a thank-you / acknowledgment was sent; null while one is still owed.</summary>
    public DateTimeOffset? AcknowledgedUtc { get; set; }
    public string AcknowledgedBy { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string DonationId => RowKey;

    public bool IsInKind => Method == DonationMethod.InKind;

    public bool NeedsAcknowledgment => AcknowledgedUtc is null;

    /// <summary>What families see on their scout's account when this gift is credited to it,
    /// unless the treasurer writes something else.</summary>
    public string DefaultCreditDescription => IsAnonymous ? "Anonymous donation" : $"Donation from {DonorName}";

    /// <summary>Gifts of this much or more usually need a written acknowledgment for the donor's
    /// taxes, so an unsent thank-you is flagged more prominently.</summary>
    public const decimal AcknowledgmentThreshold = 250m;

    public const string PartitionKeyValue = "Donation";

    public static string NewRowKey() => Guid.NewGuid().ToString("N");
}
