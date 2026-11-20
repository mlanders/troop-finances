namespace BsaTroopFinances.Data.Entities;

public static class TroopAccountKind
{
    public const string Bank = "Bank";
    public const string Venmo = "Venmo";
    public const string Cash = "Cash";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Bank, Venmo, Cash, Other];
}

/// <summary>
/// One place the troop keeps its own money: a bank account, a Venmo balance, a cash box. Its
/// balance is the opening balance plus every TroopTransaction in it; money between two accounts
/// moves as a transfer. Unlike scout accounts (ScoutLedger), this is the troop's general money.
/// </summary>
public class TroopAccountEntity
{
    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = TroopAccountKind.Bank;

    /// <summary>What was in the account on <see cref="OpeningDate"/>, when the app started keeping
    /// it. Anything received before that date is taken to be in this balance already.</summary>
    public decimal OpeningBalance { get; set; }

    // A calendar date stored as midnight UTC, like DonationEntity.ReceivedDate.
    public DateTimeOffset OpeningDate { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public string AccountId => RowKey;

    public const string PartitionKeyValue = "TroopAccount";

    public static string NewRowKey() => Guid.NewGuid().ToString("N");
}
