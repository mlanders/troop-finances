namespace BsaTroopFinances.Data.Entities;

public static class InvoicePaymentMethod
{
    public const string Check = "Check";
    public const string Cash = "Cash";
    public const string Venmo = "Venmo";
    public const string Other = "Other";
    public const string ScoutFunds = "Scout funds";

    /// <summary>Money the family pays from outside the app (everything but scout funds), in the
    /// order offered on the payment forms.</summary>
    public static readonly IReadOnlyList<string> External = [Check, Cash, Venmo, Other];

    public static bool IsValid(string method) => method == ScoutFunds || External.Contains(method);

    /// <summary>"Other" must say how it was paid, so the record is never just "Other".</summary>
    public static bool RequiresReference(string method) => method == Other;

    public static string ReferenceLabel(string method) => method switch
    {
        Check => "Check number",
        Cash => "Note (optional)",
        Venmo => "Venmo username or transaction ID",
        Other => "How was it paid?",
        _ => "Reference",
    };

    public static string ReferencePlaceholder(string method) => method switch
    {
        Check => "e.g. 1042",
        Cash => "e.g. Paid at troop meeting",
        Venmo => "e.g. @rivera-family",
        Other => "e.g. Zelle, money order",
        _ => string.Empty,
    };
}

public class InvoicePaymentEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string InvoiceId { get; set; } = string.Empty;
    public string ScoutId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset PaidUtc { get; set; }

    /// <summary>Shared by every row of one family payment (one check split across several
    /// invoices); empty for an ordinary single-invoice payment.</summary>
    public string PaymentGroupId { get; set; } = string.Empty;

    /// <summary>The whole family payment's amount (the check total); 0 when not part of one.</summary>
    public decimal GroupTotal { get; set; }

    public static string BuildPartitionKey(string invoiceId) => invoiceId;
    public static string NewRowKey(DateTimeOffset paidUtc) => $"{paidUtc.UtcTicks:D19}-{Guid.NewGuid():N}";
}
