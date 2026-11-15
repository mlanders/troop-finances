namespace BsaTroopFinances.Data.Entities;

/// <summary>
/// Money taken to the bank (or put in Venmo or the cash box) in one go: checks and cash collected
/// from families and donors, plus any other income such as fundraiser proceeds. Its lines say what
/// it held; it adds one Deposit entry to the account's register.
/// </summary>
public class DepositEntity
{
    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string AccountId { get; set; } = string.Empty;

    // A calendar date stored as midnight UTC.
    public DateTimeOffset Date { get; set; }

    public decimal Total { get; set; }
    public decimal CheckTotal { get; set; }
    public decimal CashTotal { get; set; }
    public decimal OtherTotal { get; set; }

    /// <summary>The cash counted, as JSON: {"100": 2, "20": 5, "0.25": 8, ...} -- denomination to count.</summary>
    public string CashCount { get; set; } = string.Empty;

    /// <summary>The two people who counted the money, as BSA guidance asks.</summary>
    public string CountedBy1 { get; set; } = string.Empty;
    public string CountedBy2 { get; set; } = string.Empty;

    /// <summary>RowKey of the register entry this deposit made.</summary>
    public string TransactionRowKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string DepositId => RowKey;

    public const string PartitionKeyValue = "Deposit";

    public static string NewRowKey() => Guid.NewGuid().ToString("N");
}

public static class DepositLineKind
{
    public const string InvoicePayment = "InvoicePayment";
    public const string Donation = "Donation";
    public const string Other = "Other";
}

/// <summary>
/// One item on a deposit. For an invoice payment, SourceId is the payment's RowKey -- or, for a
/// family payment split across several invoices, its PaymentGroupId, since that was one check.
/// For a donation it's the DonationId. Whether a payment or donation has been deposited is worked
/// out from these lines, so the payment and donation records themselves never change.
/// PartitionKey = DepositId.
/// </summary>
public class DepositLineEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string Kind { get; set; } = DepositLineKind.Other;
    public string SourceId { get; set; } = string.Empty;

    public string Payer { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>For other income from a fundraiser (e.g. a car wash's cash), that fundraiser.</summary>
    public string? FundraiserId { get; set; }

    public string DepositId => PartitionKey;
}
