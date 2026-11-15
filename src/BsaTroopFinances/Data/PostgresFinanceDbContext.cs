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
/// the connection string is only a placeholder. (SQL Server's context comes from the app's own
/// settings instead.)
/// </summary>
public sealed class PostgresFinanceDbContextDesignFactory : IDesignTimeDbContextFactory<PostgresFinanceDbContext>
{
    public PostgresFinanceDbContext CreateDbContext(string[] args) =>
        new(FinanceDatabase.Options(new DatabaseConnection(DatabaseProvider.Postgres, "Host=localhost;Database=troop_finances")));
}
