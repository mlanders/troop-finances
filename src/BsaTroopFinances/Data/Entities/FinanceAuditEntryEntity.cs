namespace BsaTroopFinances.Data.Entities;

public static class FinanceAuditEntityType
{
    public const string Reimbursement = "Reimbursement";
    public const string Invoice = "Invoice";
    public const string Fundraiser = "Fundraiser";
    public const string Donation = "Donation";
    public const string Scout = "Scout";
    public const string Guardian = "Guardian";
    public const string AdminUser = "AdminUser";
    public const string TroopAccount = "TroopAccount";
    public const string Deposit = "Deposit";

    /// <summary>A saved view on a list page; the entity id is the list ("invoices" or
    /// "reimbursements"), so the entry links back to that page.</summary>
    public const string ListView = "ListView";

    /// <summary>A sign-in app person linked to a scout or guardian (Families > Sign-in app links);
    /// the entity id is the sign-in app's PersonId.</summary>
    public const string SignInLink = "SignInLink";

    /// <summary>The admin page showing the record an audit entry is about. Scout/Guardian route to
    /// their list pages rather than a detail page, since the only audited action for either today
    /// is deletion -- there's no surviving detail page to link to afterward.</summary>
    public static string DetailUrl(string entityType, string entityId) => entityType switch
    {
        Reimbursement => $"/admin/reimbursements/{entityId}",
        Invoice => $"/admin/invoices/{entityId}",
        Fundraiser => $"/admin/fundraisers/{entityId}",
        Donation => $"/admin/donations/{entityId}",
        Scout => "/admin/scouts",
        Guardian => "/admin/guardians",
        AdminUser => "/admin/settings/access",
        TroopAccount => $"/admin/bank/accounts/{entityId}",
        Deposit => $"/admin/bank/deposits/{entityId}",
        ListView => $"/admin/{entityId}",
        SignInLink => "/admin/families/signin-links",
        _ => "/admin",
    };

    /// <summary>The Font Awesome icon and ".stat-card-*" color class used to represent this entity
    /// type in the dashboard and activity-history timeline views.</summary>
    public static (string Icon, string ColorClass) IconFor(string entityType) => entityType switch
    {
        Reimbursement => ("fa-sack-dollar", "stat-card-navy"),
        Invoice => ("fa-file-invoice-dollar", "stat-card-red"),
        Fundraiser => ("fa-chart-line", "stat-card-green"),
        Donation => ("fa-hand-holding-heart", "stat-card-green"),
        Scout or Guardian => ("fa-user-xmark", "stat-card-red"),
        AdminUser => ("fa-user-shield", "stat-card-gold"),
        TroopAccount => ("fa-building-columns", "stat-card-navy"),
        Deposit => ("fa-money-bill-transfer", "stat-card-green"),
        ListView => ("fa-filter", "stat-card-navy"),
        SignInLink => ("fa-link", "stat-card-navy"),
        _ => ("fa-circle-info", "stat-card-gold"),
    };
}

public class FinanceAuditEntryEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    /// <summary>SQL row version, checked on update so a stale copy can't overwrite newer changes.</summary>
    public byte[]? Version { get; set; }

    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    public static string BuildPartitionKey(string entityType, string entityId) => $"{entityType}|{entityId}";
    public static string NewRowKey(DateTimeOffset occurredUtc) => $"{occurredUtc.UtcTicks:D19}-{Guid.NewGuid():N}";
}
