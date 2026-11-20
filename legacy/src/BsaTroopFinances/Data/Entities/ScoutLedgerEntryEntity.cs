namespace BsaTroopFinances.Data.Entities;

public static class LedgerEntryType
{
    public const string Credit = "Credit";
    public const string Debit = "Debit";
}

public static class LedgerSourceType
{
    public const string FundraiserAllocation = "FundraiserAllocation";
    public const string InvoiceApplied = "InvoiceApplied";
    public const string InvoicePayment = "InvoicePayment";
    public const string InvoiceVoidReversal = "InvoiceVoidReversal";
    public const string ManualAdjustment = "ManualAdjustment";
    public const string Donation = "Donation";

    /// <summary>Money moved off a scout's account: to another scout (a debit here and a credit
    /// there, sharing one SourceId) or to the troop's general fund (just the debit -- the money
    /// was already in the troop's bank, it simply stops being held for the scout).</summary>
    public const string Transfer = "Transfer";

    /// <summary>A non-financial entry (Amount is 0) recording that the scout was renamed, so the
    /// change is part of the scout's permanent history.</summary>
    public const string NameChange = "NameChange";

    /// <summary>What a statement shows for each kind of entry.</summary>
    public static string DisplayName(string sourceType) => sourceType switch
    {
        FundraiserAllocation => "Fundraiser share",
        InvoiceApplied or InvoicePayment => "Paid toward invoice",
        InvoiceVoidReversal => "Refund from voided invoice",
        ManualAdjustment => "Adjustment",
        Donation => "Donation",
        Transfer => "Transfer",
        NameChange => "Name change",
        _ => sourceType,
    };
}

/// <summary>
/// One append-only entry in a scout's account ledger -- the source of truth for their balance
/// (ScoutEntity.CachedBalance is a denormalized read-optimization derived from this partition's
/// entries, kept in sync by IScoutLedgerRepository.AppendEntryAsync). Never mutated or deleted
/// after creation; corrections happen by appending an offsetting entry (see InvoiceVoidReversal),
/// so a scout's full financial history stays reconstructable.
/// PartitionKey = ScoutId, RowKey = zero-padded-ticks + random suffix, so ordering by RowKey
/// within a scout is chronological.
/// </summary>
public class ScoutLedgerEntryEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string EntryTypeValue { get; set; } = string.Empty;
    public string SourceTypeValue { get; set; } = string.Empty;

    /// <summary>The FundraiserEvent id, Invoice id, or empty for a manual adjustment.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Always positive -- the sign is implied by EntryTypeValue (Credit adds, Debit subtracts).</summary>
    public decimal Amount { get; set; }

    public decimal? HoursWorked { get; set; }

    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public static string BuildPartitionKey(string scoutId) => scoutId;

    /// <summary>Ticks-prefixed so entries sort chronologically within the partition for free;
    /// the Guid suffix keeps two same-tick entries from colliding.</summary>
    public static string NewRowKey() => $"{DateTimeOffset.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";

    /// <summary>The signed delta this entry applies to the scout's balance.</summary>
    public decimal SignedAmount => EntryTypeValue == LedgerEntryType.Credit ? Amount : -Amount;
}
