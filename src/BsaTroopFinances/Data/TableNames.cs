using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data;

/// <summary>
/// Table names (the same in SQL as they were in Table Storage) and the receipts blob container.
/// </summary>
public static class TableNames
{
    public const string Scouts = "Scouts";
    public const string Guardians = "Guardians";
    public const string GuardianScoutLinks = "GuardianScoutLinks";
    public const string FundraiserEvents = "FundraiserEvents";
    public const string Donations = "Donations";
    public const string TroopAccounts = "TroopAccounts";
    public const string TroopTransactions = "TroopTransactions";
    public const string Deposits = "Deposits";
    public const string DepositLines = "DepositLines";
    public const string Reconciliations = "Reconciliations";
    public const string ScoutLedger = "ScoutLedger";
    public const string ReimbursementRequests = "ReimbursementRequests";
    public const string Invoices = "Invoices";
    public const string InvoicePayments = "InvoicePayments";
    public const string FinanceAudit = "FinanceAudit";
    public const string LoginCodes = "LoginCodes";
    public const string GuardianAuthenticators = "GuardianAuthenticators";
    public const string ListViews = "ListViews";
    public const string EmailLog = "EmailLog";
    public const string EmailSettings = "EmailSettings";
    public const string AdminUsers = "AdminUsers";
    public const string AdminSignIns = "AdminSignIns";
    public const string SignInPeople = "SignInPeople";
    public const string TripImports = "TripImports";
    public const string TripImportLines = "TripImportLines";
    public const string TripPayments = "TripPayments";

    public const string ReceiptsContainer = "receipts";

    /// <summary>Small JPEG previews drawn from the receipts (see ReceiptThumbnailService). Kept
    /// apart from the receipts so backups hold only the originals.</summary>
    public const string ReceiptThumbnailsContainer = "receipt-thumbnails";

    /// <summary>
    /// The tables that hold the troop's records, with the entity each one stores, in a stable
    /// order. Used by the nightly backup and restore, and by the one-time Table Storage → SQL
    /// migration. Login codes are left out everywhere: short-lived one-time codes, nothing worth
    /// keeping or restoring. Guardian authenticator apps are left out too: they're sign-in
    /// secrets, and anyone affected by a restore can still sign in by email and link the app again.
    /// </summary>
    public static readonly IReadOnlyList<(string Table, Type Entity)> Records =
    [
        (Scouts, typeof(ScoutEntity)),
        (Guardians, typeof(GuardianEntity)),
        (GuardianScoutLinks, typeof(GuardianScoutLinkEntity)),
        (FundraiserEvents, typeof(FundraiserEventEntity)),
        (ScoutLedger, typeof(ScoutLedgerEntryEntity)),
        (ReimbursementRequests, typeof(ReimbursementRequestEntity)),
        (Invoices, typeof(InvoiceEntity)),
        (InvoicePayments, typeof(InvoicePaymentEntity)),
        (FinanceAudit, typeof(FinanceAuditEntryEntity)),
        (EmailLog, typeof(EmailLogEntity)),
        (EmailSettings, typeof(EmailSettingsEntity)),
        (AdminUsers, typeof(AdminUserEntity)),
        (AdminSignIns, typeof(AdminSignInEntity)),
        (Donations, typeof(DonationEntity)),
        (TroopAccounts, typeof(TroopAccountEntity)),
        (TroopTransactions, typeof(TroopTransactionEntity)),
        (Deposits, typeof(DepositEntity)),
        (DepositLines, typeof(DepositLineEntity)),
        (Reconciliations, typeof(ReconciliationEntity)),
        (ListViews, typeof(ListViewEntity)),
        (SignInPeople, typeof(SignInPersonEntity)),
        (TripImports, typeof(TripImportEntity)),
        (TripImportLines, typeof(TripImportLineEntity)),
        (TripPayments, typeof(TripPaymentEntity)),
    ];
}
