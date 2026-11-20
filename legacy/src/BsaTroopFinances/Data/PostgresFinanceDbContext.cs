using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BsaTroopFinances.Data;

/// <summary>
/// The same database on PostgreSQL. It adds nothing: being its own type is what gives it its own
/// migrations (in Data/PostgresMigrations), since SQL Server's can't run on PostgreSQL. Always made
/// through <see cref="FinanceDatabase"/>, and used everywhere as a <see cref="FinanceDbContext"/>.
/// </summary>
public sealed class PostgresFinanceDbContext(DbContextOptions<FinanceDbContext> options) : FinanceDbContext(options);

/// <summary>
/// For `dotnet ef migrations add ... --context PostgresFinanceDbContext`, which never connects, so
/// the connection string is only a placeholder.
/// </summary>
public sealed class PostgresFinanceDbContextDesignFactory : IDesignTimeDbContextFactory<PostgresFinanceDbContext>
{
    public PostgresFinanceDbContext CreateDbContext(string[] args) =>
        new(FinanceDatabase.Options(new DatabaseConnection(DatabaseProvider.Postgres, "Host=localhost;Database=troop_finances")));
}

/// <summary>
/// For `dotnet ef migrations add ... --context FinanceDbContext`. Without it, the tools take the
/// PostgreSQL factory above for this context too (PostgresFinanceDbContext is a FinanceDbContext)
/// and write PostgreSQL migrations into Data/Migrations. Never connects either.
/// </summary>
public sealed class FinanceDbContextDesignFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args) =>
        new(FinanceDatabase.Options(new DatabaseConnection(DatabaseProvider.SqlServer, @"Server=(localdb)\MSSQLLocalDB;Database=BsaTroopFinances;Trusted_Connection=True")));
}
