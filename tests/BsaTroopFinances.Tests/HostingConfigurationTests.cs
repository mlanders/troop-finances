using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Services;
using BsaTroopFinances.Storage;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The settings that let the app run on hosts other than Azure (see infra/HOSTING.md):
/// which file store, which email sender, where sign-in keys live, and the backup trigger.</summary>
public class HostingConfigurationTests
{
    private static readonly DatabaseConnection SqlServer = new(DatabaseProvider.SqlServer, "Server=example;Database=Troop;User Id=u;Password=p;TrustServerCertificate=True");
    private static readonly DatabaseConnection Postgres = new(DatabaseProvider.Postgres, "Host=db.example.com;Database=troop;Username=u;Password=p");

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).Build();

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    // ---- File stores ----

    [Fact]
    public void Receipts_go_to_azure_by_default_as_before()
    {
        var store = BlobStores.CreateMain(Config(("ConnectionStrings:AzureStorage", "UseDevelopmentStorage=true")));

        Assert.IsType<AzureBlobStore>(store);
    }

    [Fact]
    public void Receipts_can_go_to_an_s3_bucket_or_a_folder()
    {
        var s3 = BlobStores.CreateMain(Config(
            ("Storage:Provider", "s3"), ("Storage:S3:Bucket", "troop"), ("Storage:S3:ServiceUrl", "https://acct.r2.cloudflarestorage.com"),
            ("Storage:S3:Region", "auto"), ("Storage:S3:AccessKey", "id"), ("Storage:S3:SecretKey", "secret")));
        var folder = Path.Combine(Path.GetTempPath(), "troopfin-tests", Guid.NewGuid().ToString("N"));
        var files = BlobStores.CreateMain(Config(("Storage:Provider", "FileSystem"), ("Storage:FileSystem:Root", folder)));

        Assert.Equal("S3 bucket 'troop' at acct.r2.cloudflarestorage.com", s3.Description);
        Assert.IsType<FileSystemBlobStore>(files);
        Directory.Delete(folder);
    }

    [Theory]
    [InlineData("Dropbox", "isn't known")]
    [InlineData("S3", "Storage:S3")]
    [InlineData("FileSystem", "Storage:FileSystem:Root")]
    public void A_store_that_isnt_fully_set_up_says_what_is_missing(string provider, string mentions)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BlobStores.CreateMain(Config(("Storage:Provider", provider))));

        Assert.Contains(mentions, ex.Message);
    }

    [Fact]
    public void Backups_use_the_azure_connection_string_when_set_or_a_provider_or_are_off()
    {
        Assert.Null(BlobStores.CreateBackup(Config()));
        Assert.IsType<AzureBlobStore>(BlobStores.CreateBackup(Config(("ConnectionStrings:BackupStorage", "UseDevelopmentStorage=true"))));
        Assert.IsType<S3BlobStore>(BlobStores.CreateBackup(Config(("BackupStorage:Provider", "S3"), ("BackupStorage:S3:Bucket", "troop-backups"),
            ("BackupStorage:S3:Region", "us-west-2"), ("BackupStorage:S3:AccessKey", "id"), ("BackupStorage:S3:SecretKey", "secret"))));
    }

    [Fact]
    public void An_aws_bucket_without_a_region_says_so()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BlobStores.CreateMain(Config(("Storage:Provider", "S3"), ("Storage:S3:Bucket", "troop"))));

        Assert.Contains("Region", ex.Message);
    }

    // ---- Email ----

    [Theory]
    [InlineData(null, "Production", EmailProviders.AzureCommunicationServices)]
    [InlineData(null, "Development", EmailProviders.Console)]
    [InlineData("smtp", "Production", EmailProviders.Smtp)]
    [InlineData("Console", "Production", EmailProviders.Console)]
    [InlineData("AzureCommunicationServices", "Development", EmailProviders.AzureCommunicationServices)]
    public void Email_goes_out_the_way_the_setting_says_and_as_before_when_its_empty(string? setting, string environment, string expected)
    {
        Assert.Equal(expected, EmailProviders.Resolve(Config(("Email:Provider", setting)), Environment(environment)));
    }

    [Fact]
    public void An_unknown_email_provider_stops_startup()
    {
        Assert.Throws<InvalidOperationException>(() => EmailProviders.Resolve(Config(("Email:Provider", "Pigeon")), Environment("Production")));
    }

    [Fact]
    public void Smtp_needs_a_host_and_a_sender()
    {
        Assert.Contains("Email:Smtp:Host", Assert.Throws<InvalidOperationException>(() =>
            new SmtpEmailSender(Config(("Email:SenderAddress", "noreply@troop441.com")))).Message);
        Assert.Contains("Email:SenderAddress", Assert.Throws<InvalidOperationException>(() =>
            new SmtpEmailSender(Config(("Email:Smtp:Host", "smtp.resend.com")))).Message);
    }

    [Fact]
    public void An_smtp_message_carries_both_the_plain_and_html_versions()
    {
        using var message = SmtpEmailSender.BuildMessage("noreply@troop441.com", "maria@example.com", "Your code", "Code: 123456", "<p>Code: <b>123456</b></p>");

        Assert.Equal("noreply@troop441.com", message.From.Mailboxes.Single().Address);
        Assert.Equal("maria@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("Your code", message.Subject);
        Assert.Equal("Code: 123456", message.TextBody?.Trim());
        Assert.Equal("<p>Code: <b>123456</b></p>", message.HtmlBody?.Trim());
    }

    [Theory]
    [InlineData("Auto", SecureSocketOptions.Auto)]
    [InlineData("starttls", SecureSocketOptions.StartTls)]
    [InlineData("SslOnConnect", SecureSocketOptions.SslOnConnect)]
    [InlineData("None", SecureSocketOptions.None)]
    public void Smtp_security_settings_map_to_mailkit(string setting, SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpEmailSender.SecurityOption(new SmtpEmailOptions { Security = setting }));
    }

    // ---- Sign-in keys ----

    [Fact]
    public void Sign_in_keys_are_kept_in_the_database_under_a_fixed_app_name()
    {
        var services = new ServiceCollection().AddLogging().AddFinanceDatabase(SqlServer);
        using var provider = services.BuildServiceProvider();

        var keyManagement = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<EntityFrameworkCoreXmlRepository<FinanceDbContext>>(keyManagement.XmlRepository);
        Assert.Equal(FinanceDatabase.DataProtectionApplicationName,
            provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);

        // The key store asks for the context itself, not the factory the rest of the app uses.
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FinanceDbContext>());
    }

    [Fact]
    public void Sign_in_keys_have_room_for_their_xml_and_stay_out_of_backups()
    {
        using var db = new FinanceDbContext(FinanceDatabase.Options(SqlServer));
        var key = db.Model.FindEntityType(typeof(DataProtectionKey))!;

        Assert.Equal("DataProtectionKeys", key.GetTableName());
        Assert.Null(key.FindProperty(nameof(DataProtectionKey.Xml))!.GetMaxLength());
        Assert.DoesNotContain(TableNames.Records, r => r.Entity == typeof(DataProtectionKey));
    }

    // ---- Database engine ----

    [Theory]
    [InlineData(null, DatabaseProvider.SqlServer)]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData("postgres", DatabaseProvider.Postgres)]
    [InlineData("PostgreSQL", DatabaseProvider.Postgres)]
    public void The_database_is_sql_server_unless_the_setting_says_postgres(string? setting, DatabaseProvider expected)
    {
        Assert.Equal(expected, FinanceDatabase.Provider(Config(("Database:Provider", setting))));
    }

    [Fact]
    public void An_unknown_database_engine_stops_startup()
    {
        Assert.Contains("Database:Provider", Assert.Throws<InvalidOperationException>(() =>
            FinanceDatabase.Provider(Config(("Database:Provider", "Oracle")))).Message);
    }

    [Fact]
    public void A_tools_target_uses_the_configured_engine()
    {
        var config = Config(("Database:Provider", "Postgres"), ("ConnectionStrings:FinanceDb", "Host=localhost;Database=live"));

        Assert.Equal(new DatabaseConnection(DatabaseProvider.Postgres, "Host=localhost;Database=live"), FinanceDatabase.Connection(config));
        Assert.Equal("Host=other;Database=x", FinanceDatabase.Connection(config, "Host=other;Database=x")!.ConnectionString);
        Assert.Null(FinanceDatabase.Connection(Config()));
    }

    [Fact]
    public void Each_engine_gets_its_own_context_type_from_the_app_and_its_key_store()
    {
        using var provider = new ServiceCollection().AddLogging().AddFinanceDatabase(Postgres).BuildServiceProvider();

        using var made = provider.GetRequiredService<IDbContextFactory<FinanceDbContext>>().CreateDbContext();
        Assert.IsType<PostgresFinanceDbContext>(made);
        Assert.True(made.Database.IsNpgsql());
        using var scope = provider.CreateScope();
        Assert.IsType<PostgresFinanceDbContext>(scope.ServiceProvider.GetRequiredService<FinanceDbContext>());
        using var sqlServer = new FinanceDatabase.Factory(SqlServer).CreateDbContext();
        Assert.IsType<FinanceDbContext>(sqlServer);
        Assert.True(sqlServer.Database.IsSqlServer());
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "Server=(localdb)\\MSSQLLocalDB;Database=Troop", true)]
    [InlineData(DatabaseProvider.SqlServer, "Server=tcp:troop.database.windows.net,1433;Database=Troop", false)]
    [InlineData(DatabaseProvider.Postgres, "Host=localhost;Port=5432;Database=troop", true)]
    [InlineData(DatabaseProvider.Postgres, "Host=127.0.0.1;Database=troop", true)]
    [InlineData(DatabaseProvider.Postgres, "Host=ep-cool-name.us-east-2.aws.neon.tech;Database=troop", false)]
    [InlineData(DatabaseProvider.Postgres, "Host=localhost,db.example.com;Database=troop", false)]
    public void The_developer_tools_know_a_database_on_this_machine(DatabaseProvider provider, string connectionString, bool local)
    {
        var connection = new DatabaseConnection(provider, connectionString);

        Assert.Equal(local, connection.IsOnThisMachine);
        Assert.Equal(provider == DatabaseProvider.Postgres ? "troop" : "Troop", connection.DatabaseName);
    }

    [Fact]
    public void A_postgres_url_from_the_host_is_understood()
    {
        var connection = new DatabaseConnection(DatabaseProvider.Postgres,
            "postgresql://troop:p%40ss%3Aword@dpg-abc123-a.oregon-postgres.render.com/troop_finances?sslmode=require");

        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connection.DriverConnectionString);
        Assert.Equal("dpg-abc123-a.oregon-postgres.render.com", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("troop_finances", parsed.Database);
        Assert.Equal("troop", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal(Npgsql.SslMode.Require, parsed.SslMode);
        Assert.Equal("troop_finances", connection.DatabaseName);
        Assert.Equal("Host=localhost;Database=x", new DatabaseConnection(DatabaseProvider.Postgres, "Host=localhost;Database=x").DriverConnectionString);
        Assert.Equal(6543, new Npgsql.NpgsqlConnectionStringBuilder(new DatabaseConnection(DatabaseProvider.Postgres, "postgres://u@db:6543/d").DriverConnectionString).Port);
    }

    [Fact]
    public void On_postgres_the_row_version_is_xmin_and_times_are_stored_as_utc()
    {
        using var db = new FinanceDatabase.Factory(Postgres).CreateDbContext();
        var invoice = db.Model.FindEntityType(typeof(InvoiceEntity))!;

        var version = invoice.FindProperty("Version")!;
        Assert.Equal("xmin", version.GetColumnName());
        Assert.Equal("xid", version.GetColumnType());
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        var bytes = (byte[])version.GetValueConverter()!.ConvertFromProvider(123456u)!;
        Assert.Equal(123456u, version.GetValueConverter()!.ConvertToProvider(bytes));

        var issued = invoice.FindProperty(nameof(InvoiceEntity.IssuedUtc))!;
        var stored = (DateTimeOffset)issued.GetValueConverter()!.ConvertToProvider(new DateTimeOffset(2026, 11, 14, 18, 30, 0, TimeSpan.FromHours(-7)))!;
        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 1, 30, 0, TimeSpan.Zero), stored);
        Assert.Equal("text", db.Model.FindEntityType(typeof(ReconciliationEntity))!.FindProperty(nameof(ReconciliationEntity.Cleared))!.GetColumnType());
    }

    [Fact]
    public void On_sql_server_the_model_is_as_before()
    {
        using var db = new FinanceDatabase.Factory(SqlServer).CreateDbContext();
        var invoice = db.Model.FindEntityType(typeof(InvoiceEntity))!;

        var version = invoice.FindProperty("Version")!;
        Assert.Equal("Version", version.GetColumnName());
        Assert.Equal("rowversion", version.GetColumnType());
        Assert.Null(version.GetValueConverter());
        Assert.Null(invoice.FindProperty(nameof(InvoiceEntity.IssuedUtc))!.GetValueConverter());
    }

    [Fact]
    public void Each_engine_has_its_own_migrations_and_both_match_the_model()
    {
        using var sqlServer = new FinanceDatabase.Factory(SqlServer).CreateDbContext();
        using var postgres = new FinanceDatabase.Factory(Postgres).CreateDbContext();

        var sqlServerMigrations = sqlServer.Database.GetMigrations().ToList();
        var postgresMigrations = postgres.Database.GetMigrations().ToList();
        Assert.Contains(sqlServerMigrations, m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Contains(postgresMigrations, m => m.EndsWith("_InitialPostgres", StringComparison.Ordinal));
        Assert.Empty(sqlServerMigrations.Intersect(postgresMigrations));

        // A model change without its migration (on either engine) fails here.
        Assert.False(sqlServer.Database.HasPendingModelChanges(), "SQL Server needs a migration: see README.");
        Assert.False(postgres.Database.HasPendingModelChanges(), "PostgreSQL needs a migration: see README.");
    }

    [Fact]
    public void The_postgres_schema_script_leaves_xmin_to_postgres()
    {
        using var postgres = new FinanceDatabase.Factory(Postgres).CreateDbContext();

        var script = postgres.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE \"Invoices\"", script);
        Assert.Contains("timestamp with time zone", script);
        Assert.DoesNotContain("xmin", script);
    }

    // ---- Backup trigger ----

    [Fact]
    public void The_backup_trigger_needs_a_long_key()
    {
        Assert.Null(BackupTrigger.Key(Config()));
        Assert.Null(BackupTrigger.Key(Config(("Backup:TriggerKey", "short-and-guessable"))));
        Assert.Equal(new string('k', 32), BackupTrigger.Key(Config(("Backup:TriggerKey", new string('k', 32)))));
    }

    [Theory]
    [InlineData("Bearer 0123456789abcdef0123456789abcdef", true)]
    [InlineData("bearer 0123456789abcdef0123456789abcdef", true)]
    [InlineData("Bearer 0123456789abcdef0123456789abcdeX", false)]
    [InlineData("Bearer 0123456789abcdef", false)]
    [InlineData("Basic 0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef", false)]
    [InlineData(null, false)]
    public void The_backup_trigger_only_answers_to_its_key(string? header, bool allowed)
    {
        Assert.Equal(allowed, BackupTrigger.IsAuthorized(header, "0123456789abcdef0123456789abcdef"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("InProcess", false)]
    [InlineData("external", true)]
    public void Backups_run_in_the_app_unless_the_schedule_says_external(string? schedule, bool external)
    {
        Assert.Equal(external, BackupTrigger.IsExternalSchedule(Config(("Backup:Schedule", schedule))));
    }
}
