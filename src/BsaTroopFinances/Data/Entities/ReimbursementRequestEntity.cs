namespace BsaTroopFinances.Data.Entities;

public static class ReimbursementStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Paid = "Paid";
}

/// <summary>
/// An adult's request to be reimbursed for something they bought for the troop, with an
/// uploaded receipt. Keyed by (PartitionKey = submitting guardian, RowKey = RequestId), kept from
/// the Table Storage era so existing ids and links still work; RequestId, status and dates are
/// indexed columns, which is what the admin queue and the portal query.
/// </summary>
public class ReimbursementRequestEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string SubmitterGuardianId { get; set; } = string.Empty;
    public string SubmitterName { get; set; } = string.Empty;
    public string SubmitterEmail { get; set; } = string.Empty;

    /// <summary>Set only when a treasurer entered this request for the guardian from /admin
    /// (the admin's display name); null for a guardian's own portal submission.</summary>
    public string? SubmittedOnBehalfBy { get; set; }

    /// <summary>Optional -- a reimbursement doesn't have to be tied to one scout (e.g. a general
    /// troop supply purchase).</summary>
    public string? ScoutId { get; set; }
    public string? ScoutName { get; set; }

    public decimal AmountRequested { get; set; }
    public string Description { get; set; } = string.Empty;

    public string ReceiptBlobPath { get; set; } = string.Empty;
    public string ReceiptContentType { get; set; } = string.Empty;
    public string ReceiptOriginalFileName { get; set; } = string.Empty;
    public long ReceiptSizeBytes { get; set; }

    public string StatusValue { get; set; } = ReimbursementStatus.Pending;
    public DateTimeOffset SubmittedUtc { get; set; }

    public DateTimeOffset? ReviewedUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }

    public DateTimeOffset? PaidUtc { get; set; }
    public string? PaidBy { get; set; }
    public string? PaymentMethodNote { get; set; }

    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Set when the meeting sign-in app asked for this, not a person: "fuel:{signUpId}:{entryId}"
    /// for a driver's fuel payback (see SignInAppService.ReceiveFuelAsync). Such a request has no
    /// receipt: the amount is worked out from the miles, and the treasurer can change it.
    /// </summary>
    public string? SourceRef { get; set; }

    /// <summary>What the sign-in app last asked for. When AmountRequested differs, the treasurer
    /// changed it, and a later send from the sign-in app leaves it alone.</summary>
    public decimal? SourceAmount { get; set; }

    public bool HasReceipt => !string.IsNullOrEmpty(ReceiptBlobPath);

    public bool IsFuelPayback => SourceRef?.StartsWith(FuelSourcePrefix, StringComparison.Ordinal) == true;

    public const string FuelSourcePrefix = "fuel:";

    public static string FuelSourceRef(string signUpId, string entryId) => $"{FuelSourcePrefix}{signUpId}:{entryId}";

    public static string BuildPartitionKey(string guardianId) => guardianId;

    public static string NewRowKey() => $"{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";
}
