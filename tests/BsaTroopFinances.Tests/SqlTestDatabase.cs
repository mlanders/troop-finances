using BsaTroopFinances.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BsaTroopFinances.Tests;

/// <summary>
/// A real, throwaway database per test class: created with the app's own migrations, dropped
/// afterwards. SQL Server LocalDB by default; set TROOPFIN_TEST_POSTGRES to a PostgreSQL server's
/// connection string (no database name, e.g. "Host=127.0.0.1;Username=postgres;Password=...") to
/// run the same tests against PostgreSQL instead. Tests using it skip (rather than fail) when the
/// server isn't reachable, so `dotnet test` still works everywhere.
/// </summary>
public sealed class SqlTestDatabase : IAsyncLifetime
{
    public const string PostgresVariable = "TROOPFIN_TEST_POSTGRES";
    private const string LocalDb = @"(localdb)\MSSQLLocalDB";

    private readonly string _name = $"troopfin_tests_{Guid.NewGuid():N}";
    private readonly string? _postgresServer = Environment.GetEnvironmentVariable(PostgresVariable) is { Length: > 0 } server ? server : null;
    private DbContextOptions<FinanceDbContext>? _options;

    public bool Available { get; private set; }

    public string? UnavailableReason { get; private set; }

    public DatabaseProvider Provider => _postgresServer is null ? DatabaseProvider.SqlServer : DatabaseProvider.Postgres;

    public DatabaseConnection Connection => Provider == DatabaseProvider.Postgres
        ? new(Provider, new NpgsqlConnectionStringBuilder(_postgresServer) { Database = _name }.ConnectionString)
        : new(Provider, $"Server={LocalDb};Database={_name};Trusted_Connection=True;TrustServerCertificate=True");

    public FinanceDb Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            if (Provider == DatabaseProvider.Postgres)
            {
                await using var server = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_postgresServer) { Database = "postgres", Timeout = 15 }.ConnectionString);
                await server.OpenAsync();
            }
            else
            {
                await using var master = new SqlConnection($"Server={LocalDb};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=30");
                await master.OpenAsync();
            }

            await using var context = NewContext();
            await context.Database.MigrateAsync();
            Db = new FinanceDb(new Factory(this));
            Available = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = Provider == DatabaseProvider.Postgres
                ? $"PostgreSQL ({PostgresVariable}) isn't reachable: {ex.Message}"
                : $"SQL Server LocalDB isn't available: {ex.Message}";
        }
    }

    public FinanceDbContext NewContext() =>
        FinanceDatabase.NewContext(Provider, _options ??= new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServerOrNpgsql(Connection).Options);

    /// <summary>Empties every table so each test starts clean.</summary>
    public async Task ResetAsync()
    {
        await using var context = NewContext();
        var sql = context.GetService<ISqlGenerationHelper>();
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            // Table names come from the model, not from input.
            var statement = "DELETE FROM " + sql.DelimitIdentifier(entityType.GetTableName()!);
            await context.Database.ExecuteSqlRawAsync(statement);
        }
    }

    public async Task DisposeAsync()
    {
        if (!Available)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }

    private sealed class Factory(SqlTestDatabase owner) : IDbContextFactory<FinanceDbContext>
    {
        public FinanceDbContext CreateDbContext() => owner.NewContext();
    }
}

internal static class TestDatabaseOptions
{
    /// <summary>The app's own setup, without its retry-on-failure: a test should see the first error.</summary>
    public static DbContextOptionsBuilder<FinanceDbContext> UseSqlServerOrNpgsql(this DbContextOptionsBuilder<FinanceDbContext> builder, DatabaseConnection connection)
    {
        if (connection.Provider == DatabaseProvider.Postgres)
        {
            builder.UseNpgsql(connection.DriverConnectionString);
        }
        else
        {
            builder.UseSqlServer(connection.ConnectionString);
        }

        return builder;
    }
}
