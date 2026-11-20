using BsaTroopFinances.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BsaTroopFinances.Data;

/// <summary>
/// The app's SQL database (Azure SQL in production, SQL Server LocalDB locally; PostgreSQL through
/// <see cref="PostgresFinanceDbContext"/>, where the few differences are marked). Every table keeps
/// the (PartitionKey, RowKey) primary key the data had in Table Storage, so existing ids, links and
/// uniqueness rules (one guardian per email, one link per guardian+scout) carry over unchanged;
/// the indexes below match the queries the repositories actually run. Money is decimal(10,2).
/// Repositories don't take this directly -- they go through <see cref="FinanceDb"/>, which gives
/// each operation its own short-lived context and lets services wrap several calls in one
/// transaction.
/// It also holds ASP.NET Data Protection's keys (<see cref="DataProtectionKeys"/>), which sign
/// the admin and guardian sign-in cookies, so they survive restarts and redeploys on any host,
/// not only on Azure App Service's persistent disk.
/// </summary>
public class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>Data Protection's key ring. Deliberately left out of <see cref="TableNames.Records"/>:
    /// backup files never hold the keys, and a restore just means everyone signs in again.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<ScoutEntity> Scouts => Set<ScoutEntity>();
    public DbSet<GuardianEntity> Guardians => Set<GuardianEntity>();
    public DbSet<GuardianScoutLinkEntity> GuardianScoutLinks => Set<GuardianScoutLinkEntity>();
    public DbSet<FundraiserEventEntity> FundraiserEvents => Set<FundraiserEventEntity>();
    public DbSet<DonationEntity> Donations => Set<DonationEntity>();
    public DbSet<TroopAccountEntity> TroopAccounts => Set<TroopAccountEntity>();
    public DbSet<TroopTransactionEntity> TroopTransactions => Set<TroopTransactionEntity>();
    public DbSet<DepositEntity> Deposits => Set<DepositEntity>();
    public DbSet<DepositLineEntity> DepositLines => Set<DepositLineEntity>();
    public DbSet<ReconciliationEntity> Reconciliations => Set<ReconciliationEntity>();
    public DbSet<ScoutLedgerEntryEntity> ScoutLedger => Set<ScoutLedgerEntryEntity>();
    public DbSet<ReimbursementRequestEntity> ReimbursementRequests => Set<ReimbursementRequestEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<InvoicePaymentEntity> InvoicePayments => Set<InvoicePaymentEntity>();
    public DbSet<FinanceAuditEntryEntity> FinanceAudit => Set<FinanceAuditEntryEntity>();
    public DbSet<LoginCodeEntity> LoginCodes => Set<LoginCodeEntity>();
    public DbSet<GuardianAuthenticatorEntity> GuardianAuthenticators => Set<GuardianAuthenticatorEntity>();
    public DbSet<ListViewEntity> ListViews => Set<ListViewEntity>();
    public DbSet<EmailLogEntity> EmailLog => Set<EmailLogEntity>();
    public DbSet<EmailSettingsEntity> EmailSettings => Set<EmailSettingsEntity>();
    public DbSet<AdminUserEntity> AdminUsers => Set<AdminUserEntity>();
    public DbSet<AdminSignInEntity> AdminSignIns => Set<AdminSignInEntity>();
    public DbSet<SignInPersonEntity> SignInPeople => Set<SignInPersonEntity>();
    public DbSet<TripImportEntity> TripImports => Set<TripImportEntity>();
    public DbSet<TripImportLineEntity> TripImportLines => Set<TripImportLineEntity>();
    public DbSet<TripPaymentEntity> TripPayments => Set<TripPaymentEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(400);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ScoutEntity>(e =>
        {
            Keys(e);
            e.ToTable("Scouts");
            e.HasIndex(s => s.IsActive);
        });

        model.Entity<GuardianEntity>(e =>
        {
            Keys(e);
            e.ToTable("Guardians");
            e.HasIndex(g => g.GuardianId).IsUnique();
        });

        model.Entity<GuardianScoutLinkEntity>(e =>
        {
            Keys(e);
            e.ToTable("GuardianScoutLinks");
            e.HasIndex(l => l.ScoutId);
        });

        model.Entity<FundraiserEventEntity>(e =>
        {
            Keys(e);
            e.ToTable("FundraiserEvents");
            e.Property(f => f.Description).HasMaxLength(2000);
            e.HasIndex(f => f.EventDate);
        });

        model.Entity<DonationEntity>(e =>
        {
            Keys(e);
            e.ToTable("Donations");
            e.Property(d => d.DonorAddress).HasMaxLength(1000);
            e.Property(d => d.Purpose).HasMaxLength(2000);
            e.Property(d => d.Notes).HasMaxLength(2000);
            e.HasIndex(d => d.ReceivedDate);
            e.Ignore(d => d.DonationId);
            e.Ignore(d => d.IsInKind);
            e.Ignore(d => d.NeedsAcknowledgment);
            e.Ignore(d => d.DefaultCreditDescription);
        });

        model.Entity<TroopAccountEntity>(e =>
        {
            Keys(e);
            e.ToTable("TroopAccounts");
            e.Ignore(a => a.AccountId);
        });

        model.Entity<TroopTransactionEntity>(e =>
        {
            Keys(e);
            e.ToTable("TroopTransactions");
            e.Property(t => t.Description).HasMaxLength(2000);
            e.HasIndex(t => new { t.SourceType, t.SourceId });
            e.HasIndex(t => t.ReconciliationId);
            e.Ignore(t => t.AccountId);
            e.Ignore(t => t.TransactionId);
            e.Ignore(t => t.IsCleared);
            e.Ignore(t => t.HasReceipt);
        });

        model.Entity<DepositEntity>(e =>
        {
            Keys(e);
            e.ToTable("Deposits");
            e.Property(d => d.CashCount).HasMaxLength(1000);
            e.HasIndex(d => d.Date);
            e.Ignore(d => d.DepositId);
        });

        model.Entity<DepositLineEntity>(e =>
        {
            Keys(e);
            e.ToTable("DepositLines");
            e.Property(l => l.Description).HasMaxLength(2000);
            e.HasIndex(l => new { l.Kind, l.SourceId });
            e.Ignore(l => l.DepositId);
        });

        model.Entity<ReconciliationEntity>(e =>
        {
            Keys(e);
            e.ToTable("Reconciliations");
            e.Property(r => r.Cleared).HasColumnType(Database.IsNpgsql() ? "text" : "nvarchar(max)");
            e.Ignore(r => r.AccountId);
            e.Ignore(r => r.ReconciliationId);
            e.Ignore(r => r.ClearedIds);
        });

        model.Entity<ScoutLedgerEntryEntity>(e =>
        {
            Keys(e);
            e.ToTable("ScoutLedger");
            e.Property(l => l.Description).HasMaxLength(2000);
            e.HasIndex(l => new { l.SourceTypeValue, l.SourceId });
            e.HasIndex(l => l.CreatedUtc);
        });

        model.Entity<ReimbursementRequestEntity>(e =>
        {
            Keys(e);
            e.ToTable("ReimbursementRequests");
            e.Property(r => r.Description).HasMaxLength(2000);
            e.Property(r => r.ReviewNotes).HasMaxLength(2000);
            e.HasIndex(r => r.RequestId).IsUnique();
            e.HasIndex(r => new { r.StatusValue, r.SubmittedUtc });
            e.HasIndex(r => r.SubmittedUtc);
            e.HasIndex(r => r.PaidUtc);
            e.HasIndex(r => r.ScoutId);
        });

        model.Entity<InvoiceEntity>(e =>
        {
            Keys(e);
            e.ToTable("Invoices");
            e.Property(i => i.Description).HasMaxLength(2000);
            e.Property(i => i.VoidReason).HasMaxLength(2000);
            e.HasIndex(i => i.InvoiceId).IsUnique();
            e.HasIndex(i => new { i.StatusValue, i.IssuedUtc });
            e.HasIndex(i => i.IssuedUtc);
            e.HasIndex(i => i.ScoutId);
            e.HasIndex(i => i.GuardianId);
            e.Ignore(i => i.IsForGuardian);
            e.Ignore(i => i.BilledTo);
            e.Ignore(i => i.OwnerId);
        });

        model.Entity<InvoicePaymentEntity>(e =>
        {
            Keys(e);
            e.ToTable("InvoicePayments");
            e.HasIndex(p => p.InvoiceId);
            e.HasIndex(p => p.PaidUtc);
        });

        model.Entity<FinanceAuditEntryEntity>(e =>
        {
            Keys(e);
            e.ToTable("FinanceAudit");
            e.Property(a => a.Notes).HasMaxLength(4000);
            e.Property(a => a.Details).HasMaxLength(4000);
            e.HasIndex(a => a.OccurredUtc);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
        });

        model.Entity<LoginCodeEntity>(e =>
        {
            Keys(e);
            e.ToTable("LoginCodes");
        });

        model.Entity<GuardianAuthenticatorEntity>(e =>
        {
            Keys(e);
            e.ToTable(TableNames.GuardianAuthenticators);
            e.Property(a => a.SecretProtected).HasMaxLength(200);
        });

        model.Entity<ListViewEntity>(e =>
        {
            Keys(e);
            e.ToTable(TableNames.ListViews);
            e.Ignore(v => v.List);
            e.Ignore(v => v.ViewId);
            e.Property(v => v.Name).HasMaxLength(80);
            e.Property(v => v.Status).HasMaxLength(40);
            e.Property(v => v.NameSearch).HasMaxLength(200);
            e.Property(v => v.DescriptionSearch).HasMaxLength(200);
            e.Property(v => v.CreatedBy).HasMaxLength(254);
        });

        model.Entity<EmailLogEntity>(e =>
        {
            Keys(e);
            e.ToTable("EmailLog");
            e.Property(l => l.Error).HasMaxLength(4000);
            e.HasIndex(l => l.SentUtc);
        });

        model.Entity<EmailSettingsEntity>(e =>
        {
            Keys(e);
            e.ToTable("EmailSettings");
            e.Property(s => s.AdminRecipients).HasMaxLength(4000);
        });

        model.Entity<AdminUserEntity>(e =>
        {
            Keys(e);
            e.ToTable("AdminUsers");
            e.HasIndex(u => u.Role);
            e.Ignore(u => u.UserKey);
            e.Ignore(u => u.HasAccess);
            e.Property(u => u.EditReason).HasMaxLength(500);
        });

        model.Entity<AdminSignInEntity>(e =>
        {
            Keys(e);
            e.ToTable("AdminSignIns");
            e.Property(s => s.UserKey).HasMaxLength(254);
            e.Property(s => s.VerifiedEmail).HasMaxLength(254);
            e.HasIndex(s => s.UserKey);
            e.HasIndex(s => s.VerifiedEmail);
            e.Ignore(s => s.SignInKey);
        });

        // The meeting sign-in app's roster, trips and payments (see Services/SignInAppService).
        // Unique on the linked scout and guardian, so one finance person can't stand for two
        // sign-in people (SQL Server leaves unlinked NULLs out of the index; PostgreSQL never
        // counts NULLs as duplicates).
        model.Entity<SignInPersonEntity>(e =>
        {
            Keys(e);
            e.ToTable(TableNames.SignInPeople);
            e.HasIndex(p => p.LinkedScoutId).IsUnique();
            e.HasIndex(p => p.LinkedGuardianId).IsUnique();
            e.Ignore(p => p.PersonId);
            e.Ignore(p => p.IsAdult);
            e.Ignore(p => p.LinkedId);
            e.Ignore(p => p.IsLinked);
        });

        model.Entity<TripImportEntity>(e =>
        {
            Keys(e);
            e.ToTable(TableNames.TripImports);
            e.HasIndex(t => t.StartDate);
            e.Ignore(t => t.SignUpId);
            e.Ignore(t => t.InvoiceDescription);
        });

        model.Entity<TripImportLineEntity>(e =>
        {
            Keys(e);
            e.ToTable(TableNames.TripImportLines);
            e.HasIndex(l => l.SignInPersonId);
            e.HasIndex(l => l.InvoiceId);
            e.Ignore(l => l.SignUpId);
            e.Ignore(l => l.EntryId);
            e.Ignore(l => l.IsAdult);
            e.Ignore(l => l.HasInvoice);
        });

        model.Entity<TripPaymentEntity>(e =>
        {
            Keys(e);
            e.ToTable(TableNames.TripPayments);
            e.HasIndex(p => p.InvoiceId);
            e.Ignore(p => p.SignUpId);
            e.Ignore(p => p.PaymentId);
        });

        // Each key is an XML document of a kilobyte or more, past the 400-character default above.
        model.Entity<DataProtectionKey>(e =>
        {
            e.ToTable("DataProtectionKeys");
            e.Property(k => k.FriendlyName).HasMaxLength(400);
            e.Property(k => k.Xml).Metadata.SetMaxLength(null);
        });

        if (Database.IsNpgsql())
        {
            // PostgreSQL only stores a time with no offset (always UTC); writing one that has an
            // offset fails. SQL Server keeps the offset, but every time here is meant as a moment,
            // so on PostgreSQL each is turned into UTC on the way in.
            foreach (var property in model.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            {
                property.SetValueConverter(ToUtc);
            }
        }
    }

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> ToUtc = new(v => v.ToUniversalTime(), v => v);

    /// <summary>PostgreSQL's xmin (a 32-bit number) as the 4-byte version the entities carry.</summary>
    private static readonly ValueConverter<byte[]?, uint> XminAsBytes = new(v => BitConverter.ToUInt32(v!, 0), v => BitConverter.GetBytes(v));

    /// <summary>The Table Storage-era composite key, plus the row version used for optimistic concurrency.</summary>
    private void Keys<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e) where T : class
    {
        e.HasKey("PartitionKey", "RowKey");
        // 100 + 254 characters (an email address can be up to 254) stays under SQL Server's
        // 900-byte limit for a clustered key.
        e.Property<string>("PartitionKey").HasMaxLength(100);
        e.Property<string>("RowKey").HasMaxLength(254);
        if (Database.IsNpgsql())
        {
            // PostgreSQL has no rowversion type. Every row already has xmin, the id of the
            // transaction that last wrote it, which changes whenever the row does: it serves as
            // the version (and, being a system column, is never created by a migration).
            e.Property<byte[]?>("Version").HasColumnName("xmin").HasColumnType("xid").HasConversion(XminAsBytes)
                .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        }
        else
        {
            e.Property<byte[]?>("Version").IsRowVersion();
        }
    }
}
