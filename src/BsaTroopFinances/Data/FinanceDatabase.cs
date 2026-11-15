using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BsaTroopFinances.Data;

/// <summary>The database engines the app runs on. Each has its own set of migrations.</summary>
public enum DatabaseProvider
{
    /// <summary>Azure SQL, SQL Server (in a container or on a server), or LocalDB. The default.</summary>
    SqlServer,

    /// <summary>PostgreSQL: Neon, Supabase, Railway, Render, Fly, or a container.</summary>
    Postgres,
}

/// <summary>Which engine and how to reach it.</summary>
public sealed record DatabaseConnection(DatabaseProvider Provider, string ConnectionString)
{
    /// <summary>The server's name as the connection string gives it, for messages.</summary>
    public string Server => Provider == DatabaseProvider.Postgres
        ? new NpgsqlConnectionStringBuilder(DriverConnectionString).Host ?? ""
        : new SqlConnectionStringBuilder(ConnectionString).DataSource;

    /// <summary>The database's name, for messages.</summary>
    public string DatabaseName => Provider == DatabaseProvider.Postgres
        ? new NpgsqlConnectionStringBuilder(DriverConnectionString).Database ?? ""
        : new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    /// <summary>
    /// The connection string in the form the database driver takes. Hosts often hand out a
    /// PostgreSQL address as a URL (postgresql://user:password@host:5432/db?sslmode=require: Render,
    /// Railway, Neon, Supabase); that's turned into Npgsql's Host=...;Database=... form.
    /// </summary>
    public string DriverConnectionString =>
        Provider == DatabaseProvider.Postgres
        && (ConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || ConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            ? FromPostgresUrl(ConnectionString)
            : ConnectionString;

    private static string FromPostgresUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
        };
        if (userInfo.Length > 1)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var (key, value) = pair.Split('=', 2) is [var k, var v] ? (Uri.UnescapeDataString(k), Uri.UnescapeDataString(v)) : (pair, "");
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<SslMode>(value.Replace("-", ""), ignoreCase: true, out var mode))
            {
                builder.SslMode = mode;
            }
        }

        return builder.ConnectionString;
    }

    private static readonly string[] LocalSqlServers = ["(localdb)", "(local)", "localhost", "127.0.0.1", "."];
    private static readonly string[] LocalPostgresHosts = ["localhost", "127.0.0.1", "::1"];

    /// <summary>True for a database on this machine (the developer tools refuse to write anywhere
    /// else unless told to).</summary>
    public bool IsOnThisMachine => Provider == DatabaseProvider.Postgres
        ? Server.Split(',').All(host => LocalPostgresHosts.Contains(host.Split(':')[0].Trim(), StringComparer.OrdinalIgnoreCase) || host.StartsWith('/'))
        : LocalSqlServers.Any(local => Server.StartsWith(local, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The one place that says which database engine the app talks to (Database:Provider; see
/// infra/HOSTING.md): SQL Server -- Azure SQL in Azure, LocalDB locally, or SQL Server in a
/// container -- by default, or PostgreSQL. The web app and the command-line tools all connect
/// through here, so each engine always gets its own context type (<see cref="FinanceDbContext"/>
/// or <see cref="PostgresFinanceDbContext"/>) and with it its own migrations.
/// </summary>
public static class FinanceDatabase
{
    public const string ConnectionStringName = "FinanceDb";
    public const string ProviderSetting = "Database:Provider";

    /// <summary>Data Protection's application name. Keys are only shared between apps with the same
    /// name; without one it defaults to the app's folder on disk, which differs from host to host.</summary>
    public const string DataProtectionApplicationName = "BsaTroopFinances";

    /// <summary>Database:Provider: "SqlServer" (or empty) or "Postgres" ("PostgreSQL" works too).</summary>
    public static DatabaseProvider Provider(IConfiguration configuration)
    {
        var setting = configuration[ProviderSetting]?.Trim();
        if (string.IsNullOrEmpty(setting) || setting.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.SqlServer;
        }

        if (setting.Equals("Postgres", StringComparison.OrdinalIgnoreCase) || setting.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseProvider.Postgres;
        }

        throw new InvalidOperationException($"{ProviderSetting} '{setting}' isn't known. Use SqlServer (the default) or Postgres.");
    }

    /// <summary>The configured engine with ConnectionStrings:FinanceDb, or with
    /// <paramref name="connectionString"/> when given (a tool's --target); null when there's neither.</summary>
    public static DatabaseConnection? Connection(IConfiguration configuration, string? connectionString = null)
    {
        connectionString ??= configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(connectionString) ? null : new DatabaseConnection(Provider(configuration), connectionString);
    }

    /// <summary>
    /// Registers the database for the web app: a context factory (each operation gets its own
    /// short-lived context), the context itself as scoped (Data Protection's key store asks for
    /// it), <see cref="FinanceDb"/>'s shared transactions, and Data Protection's keys kept in the
    /// database. Those keys sign the admin and guardian sign-in cookies; kept on local disk instead
    /// (the default), a host without a persistent disk would make new ones on every restart and
    /// sign everyone out.
    /// </summary>
    public static IServiceCollection AddFinanceDatabase(this IServiceCollection services, DatabaseConnection connection)
    {
        var factory = new Factory(connection);
        services.AddSingleton(connection);
        services.AddSingleton<IDbContextFactory<FinanceDbContext>>(factory);
        services.AddScoped(_ => factory.CreateDbContext());
        services.AddSingleton<FinanceDb>();
        services.AddSingleton<IUnitOfWork>(sp => sp.GetRequiredService<FinanceDb>());

        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<FinanceDbContext>();
        return services;
    }

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, DatabaseConnection connection) =>
        connection.Provider switch
        {
            DatabaseProvider.Postgres => options.UseNpgsql(connection.DriverConnectionString, pg => pg.EnableRetryOnFailure()),
            _ => options.UseSqlServer(connection.ConnectionString, sql => sql.EnableRetryOnFailure()),
        };

    public static DbContextOptions<FinanceDbContext> Options(DatabaseConnection connection)
    {
        var builder = new DbContextOptionsBuilder<FinanceDbContext>();
        Configure(builder, connection);
        return builder.Options;
    }

    /// <summary>A context of the right type for the engine: the type is what picks the migrations.</summary>
    public static FinanceDbContext NewContext(DatabaseProvider provider, DbContextOptions<FinanceDbContext> options) =>
        provider == DatabaseProvider.Postgres ? new PostgresFinanceDbContext(options) : new FinanceDbContext(options);

    /// <summary>Makes contexts for one database: the web app's (from DI) and the command-line tools'.</summary>
    public sealed class Factory(DatabaseConnection connection) : IDbContextFactory<FinanceDbContext>
    {
        private readonly DbContextOptions<FinanceDbContext> _options = Options(connection);

        public DatabaseConnection Connection => connection;

        public FinanceDbContext CreateDbContext() => NewContext(connection.Provider, _options);
    }
}
