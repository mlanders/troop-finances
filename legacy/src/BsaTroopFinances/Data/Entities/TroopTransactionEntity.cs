namespace BsaTroopFinances.Data.Entities;

public static class TroopTransactionKind
{
    public const string Deposit = "Deposit";
    public const string Expense = "Expense";
    public const string ReimbursementPayout = "Reimbursement";
    public const string TransferIn = "Transfer in";
    public const string TransferOut = "Transfer out";
    public const string BankFee = "Bank fee";
    public const string Interest = "Interest";
    public const string OtherIncome = "Other income";
    public const string Adjustment = "Adjustment";

    /// <summary>Entries typed straight into the register (the rest come from a deposit, a
    /// reimbursement or a transfer), with whether each is money in.</summary>
    public static readonly IReadOnlyList<(string Kind, bool MoneyIn)> Manual =
    [
        (Expense, false), (BankFee, false), (Interest, true), (OtherIncome, true),
    ];

    public static bool IsManual(string kind) => Manual.Any(m => m.Kind == kind) || kind == Adjustment;
}

/// <summary>
/// One line in a troop account's register: money in (positive Amount) or out (negative). Entries
/// made by a deposit, a reimbursement payout or a transfer point back at it through
/// SourceType/SourceId. Once a reconciliation has cleared an entry (ReconciliationId set) it is
/// locked, so the reconciled balance can't change behind the treasurer's back.
/// PartitionKey = AccountId, RowKey = ticks-prefixed so an account's entries sort by when they
/// were made.
/// </summary>
public class TroopTransactionEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    // A calendar date stored as midnight UTC: the date on the bank statement.
    public DateTimeOffset Date { get; set; }

    /// <summary>Signed: positive is money into the account, negative money out.</summary>
    public decimal Amount { get; set; }

    public string Kind { get; set; } = TroopTransactionKind.Expense;

    /// <summary>Who was paid, or who the money came from.</summary>
    public string Payee { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Check number or other reference.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>What made the entry (see TroopTransactionSource), and that record's id: the
    /// deposit, the reimbursement request, or the id shared by both halves of a transfer.</summary>
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;

    public string ReceiptBlobPath { get; set; } = string.Empty;
    public string ReceiptContentType { get; set; } = string.Empty;
    public string ReceiptFileName { get; set; } = string.Empty;

    /// <summary>The reconciliation that matched this entry to a bank statement; null until then.</summary>
    public string? ReconciliationId { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string AccountId => PartitionKey;
    public string TransactionId => RowKey;
    public bool IsCleared => ReconciliationId is not null;
    public bool HasReceipt => !string.IsNullOrEmpty(ReceiptBlobPath);

    public static string NewRowKey() => $"{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";
}

public static class TroopTransactionSource
{
    public const string Deposit = "Deposit";
    public const string Reimbursement = "Reimbursement";
    public const string Transfer = "Transfer";
}
