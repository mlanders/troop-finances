namespace BsaTroopFinances.Data.Entities;

public static class InvoiceStatus
{
    public const string Outstanding = "Outstanding";
    public const string Paid = "Paid";
    public const string Voided = "Voided";
}

/// <summary>
/// A charge issued to a scout (a trip, a meal, summer camp, etc.). Some or all of TotalAmount
/// can be applied against the scout's account balance at issue time (AmountAppliedFromAccount,
/// clamped to the balance available then); whatever's left (AmountDueFromFamily) is owed outside
/// the app and gets marked paid manually. An invoice can instead be billed to an adult (see
/// GuardianId). Keyed by (PartitionKey = ScoutId, or GuardianId for an adult's, RowKey = InvoiceId),
/// kept from the Table Storage era so existing ids and links still work; ScoutId and InvoiceId
/// are also their own indexed columns, which is what queries use.
/// </summary>
public class InvoiceEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string ScoutId { get; set; } = string.Empty;
    public string ScoutName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    /// <summary>Total applied against the scout's account balance: the amount clamped at issue
    /// time plus any later payments recorded with <see cref="InvoicePaymentMethod.ScoutFunds"/>.</summary>
    public decimal AmountAppliedFromAccount { get; set; }

    /// <summary>Stored, not computed on read, so reporting (e.g. "all outstanding amounts") can
    /// query/sum this field directly instead of recomputing TotalAmount - AmountAppliedFromAccount
    /// every time.</summary>
    public decimal AmountDueFromFamily { get; set; }

    /// <summary>Set only when the invoice is billed to an adult rather than a scout (e.g. a
    /// parent's own place at summer camp). ScoutId and ScoutName are then empty, PartitionKey is
    /// this guardian's id, and scout funds can never be applied to it.</summary>
    public string? GuardianId { get; set; }

    /// <summary>The guardian's name when the invoice was issued (see <see cref="GuardianId"/>).</summary>
    public string? GuardianName { get; set; }

    public bool IsForGuardian => !string.IsNullOrEmpty(GuardianId);

    /// <summary>Who the charge is for: the scout, or the adult on a guardian invoice.</summary>
    public string BilledTo => IsForGuardian ? GuardianName ?? string.Empty : ScoutName;

    /// <summary>The id the invoice is filed under (its PartitionKey): the scout's, or the
    /// guardian's for a guardian invoice. With the invoice id it makes a direct key lookup.</summary>
    public string OwnerId => PartitionKey;

    public string StatusValue { get; set; } = InvoiceStatus.Outstanding;

    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string IssuedBy { get; set; } = string.Empty;

    public DateTimeOffset? PaidUtc { get; set; }
    public string? PaidBy { get; set; }

    public DateTimeOffset? VoidedUtc { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    /// <summary>RowKey of the ScoutLedger debit entry this invoice created (when
    /// AmountAppliedFromAccount > 0), so Void can find and reverse exactly that entry.</summary>
    public string? LedgerEntryRowKey { get; set; }

    public string InvoiceId { get; set; } = string.Empty;

    public static string BuildPartitionKey(string scoutId) => scoutId;

    public static string NewRowKey() => $"{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";
}
