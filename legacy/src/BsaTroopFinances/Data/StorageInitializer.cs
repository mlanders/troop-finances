using BsaTroopFinances.Storage;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data;

/// <summary>
/// Brings storage up to date on startup, so neither a developer machine nor a fresh deployment
/// needs manual setup: applies any pending EF Core migrations to the SQL database (creating it
/// locally if needed), and creates the private receipts and thumbnails containers.
/// </summary>
public class StorageInitializer(
    IDbContextFactory<FinanceDbContext> dbContextFactory,
    IBlobStore store,
    ILogger<StorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation(pending.Count == 0
                ? "Finance database schema is up to date"
                : $"Applied {pending.Count} database migration(s): {string.Join(", ", pending)}");
        }

        await store.EnsureContainerAsync(TableNames.ReceiptsContainer, cancellationToken);
        await store.EnsureContainerAsync(TableNames.ReceiptThumbnailsContainer, cancellationToken);
        logger.LogInformation("Receipts are kept in {Store} ({Container} and {Thumbnails}, private)",
            store.Description, TableNames.ReceiptsContainer, TableNames.ReceiptThumbnailsContainer);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
