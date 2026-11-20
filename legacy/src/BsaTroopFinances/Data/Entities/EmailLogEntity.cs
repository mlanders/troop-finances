namespace BsaTroopFinances.Data.Entities;

public static class EmailType
{
    public const string LoginCode = "LoginCode";
    public const string ReceiptSubmitted = "ReceiptSubmitted";
    public const string ReceiptStatus = "ReceiptStatus";
    public const string Invoice = "Invoice";
    public const string EditorGranted = "EditorGranted";
    public const string TemporaryEdit = "TemporaryEdit";
    public const string AuthenticatorChanged = "AuthenticatorChanged";

    public static string DisplayName(string emailType) => emailType switch
    {
        LoginCode => "Login code",
        ReceiptSubmitted => "Receipt submitted",
        ReceiptStatus => "Receipt status update",
        Invoice => "Invoice",
        EditorGranted => "Edit access granted",
        TemporaryEdit => "Temporary edit turned on",
        AuthenticatorChanged => "Authenticator app added or removed",
        _ => emailType,
    };
}

public static class EmailLogStatus
{
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

/// <summary>
/// One row per email the app attempted to send (one per recipient). Deliberately a summary only
/// -- no message body -- so login codes are never stored. Keyed by (PartitionKey = yyyyMM sent,
/// RowKey = inverted ticks), kept from the Table Storage era; queries go through the SentUtc index.
/// </summary>
public class EmailLogEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string EmailType { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = EmailLogStatus.Sent;
    public string Error { get; set; } = string.Empty;
    public DateTimeOffset SentUtc { get; set; }

    /// <summary>The reimbursement request, invoice or admin user this email was about; empty for login codes.</summary>
    public string RelatedEntityId { get; set; } = string.Empty;

    public static string BuildPartitionKey(DateTimeOffset sentUtc) => sentUtc.UtcDateTime.ToString("yyyyMM");

    public static string NewRowKey(DateTimeOffset sentUtc) =>
        $"{DateTimeOffset.MaxValue.UtcTicks - sentUtc.UtcTicks:D19}-{Guid.NewGuid():N}";
}
