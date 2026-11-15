using System.Text;
using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using BsaTroopFinances.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BsaTroopFinances.Tests;

/// <summary>The nightly backup and restore against a real SQL database (LocalDB; skips unless it's
/// available), with receipts and backups each in their own temporary folder.</summary>
public class SqlBackupTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>, IAsyncLifetime
{
    private static readonly DateTimeOffset When = new(2026, 9, 15, 18, 0, 0, TimeSpan.Zero);
    private readonly TestBlobStores.TestStore _main = TestBlobStores.TemporaryFolder();
    private readonly TestBlobStores.TestStore _backups = TestBlobStores.TemporaryFolder();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _main.DisposeAsync();
        await _backups.DisposeAsync();
    }

    private sealed class Factory(SqlTestDatabase sql) : IDbContextFactory<FinanceDbContext>
    {
        public FinanceDbContext CreateDbContext() => sql.NewContext();
    }

    private async Task<(BackupService Service, IBlobStore Main)> ReadyAsync()
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();

        var main = _main.Store;
        var backups = _backups.Store;
        await main.EnsureContainerAsync(TableNames.ReceiptsContainer);
        using (var receipt = new MemoryStream("receipt-bytes"u8.ToArray()))
        {
            await main.UploadAsync(TableNames.ReceiptsContainer, "maria/req1.pdf", receipt, "application/pdf");
        }

        var scouts = new ScoutRepository(sql.Db, TimeProvider.System);
        await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "alex", FullName = "Alex Rivera", CachedBalance = 50.25m, CreatedUtc = When });
        await new InvoiceRepository(sql.Db).AddAsync(new InvoiceEntity
        {
            PartitionKey = "alex", RowKey = "inv1", InvoiceId = "inv1", ScoutId = "alex", ScoutName = "Alex Rivera", Description = "Camp",
            TotalAmount = 275m, AmountAppliedFromAccount = 100m, AmountDueFromFamily = 175m, StatusValue = InvoiceStatus.Outstanding,
            IssuedUtc = When, DueDate = When.AddDays(30),
        });
        await new ReimbursementRequestRepository(sql.Db).AddAsync(new ReimbursementRequestEntity
        {
            PartitionKey = "maria", RowKey = "req1", RequestId = "req1", SubmitterName = "Maria Rivera", AmountRequested = 42.18m,
            StatusValue = ReimbursementStatus.Pending, SubmittedUtc = When, ReceiptBlobPath = "maria/req1.pdf",
        });

        var service = new BackupService(new Factory(sql), main, backups, TimeProvider.System, NullLogger<BackupService>.Instance);
        return (service, main);
    }

    [SkippableFact]
    public async Task Nightly_backup_exports_the_sql_tables_and_receipts()
    {
        var (service, _) = await ReadyAsync();

        var result = await service.RunBackupAsync();

        Assert.True(result.Succeeded, result.Message);
        var manifest = result.Manifest!;
        Assert.Equal(BackupService.SqlFormat, manifest.Format);
        Assert.Equal(1, manifest.TableRowCounts[TableNames.Scouts]);
        Assert.Equal(1, manifest.TableRowCounts[TableNames.Invoices]);
        Assert.Equal(1, manifest.TableRowCounts[TableNames.ReimbursementRequests]);
        Assert.Equal(1, manifest.ReceiptCount);
        Assert.Equal(manifest.Date, (await service.ListBackupsAsync())[0].Date);
    }

    [SkippableFact]
    public async Task Restore_replace_all_puts_the_data_back_exactly()
    {
        var (service, main) = await ReadyAsync();
        var date = (await service.RunBackupAsync()).Manifest!.Date;

        // Damage it: change an amount, delete a scout, add a stray row, lose the receipt.
        await using (var db = sql.NewContext())
        {
            var invoice = await db.Invoices.SingleAsync();
            invoice.AmountDueFromFamily = 1m;
            db.Scouts.Remove(await db.Scouts.SingleAsync());
            db.Scouts.Add(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "stray", FullName = "Stray" });
            await db.SaveChangesAsync();
        }
        await main.DeleteAsync(TableNames.ReceiptsContainer, "maria/req1.pdf");

        await service.RestoreAsync(date, replaceAll: true, _ => { });

        await using var check = sql.NewContext();
        var scout = await check.Scouts.SingleAsync();
        Assert.Equal("alex", scout.RowKey);
        Assert.Equal(50.25m, scout.CachedBalance);
        var restored = await check.Invoices.SingleAsync();
        Assert.Equal(175m, restored.AmountDueFromFamily);
        Assert.Equal(When.AddDays(30), restored.DueDate);
        Assert.Equal(42.18m, (await check.ReimbursementRequests.SingleAsync()).AmountRequested);
        var receipt = await TestBlobStores.ReadAllAsync(main, TableNames.ReceiptsContainer, "maria/req1.pdf");
        Assert.Equal("receipt-bytes", Encoding.UTF8.GetString(receipt!));
    }

    [SkippableFact]
    public async Task Restore_upsert_overwrites_matching_rows_and_keeps_newer_ones()
    {
        var (service, _) = await ReadyAsync();
        var date = (await service.RunBackupAsync()).Manifest!.Date;

        await using (var db = sql.NewContext())
        {
            (await db.Invoices.SingleAsync()).Description = "Edited after the backup";
            db.Scouts.Add(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "newer", FullName = "Added later" });
            await db.SaveChangesAsync();
        }

        await service.RestoreAsync(date, replaceAll: false, _ => { });

        await using var check = sql.NewContext();
        Assert.Equal("Camp", (await check.Invoices.SingleAsync()).Description);
        Assert.Equal(2, await check.Scouts.CountAsync());
    }

    [SkippableFact]
    public async Task Roster_import_brings_only_scouts_guardians_and_links_with_zero_balances()
    {
        var (service, _) = await ReadyAsync();
        await using (var db = sql.NewContext())
        {
            db.Guardians.Add(new GuardianEntity { PartitionKey = GuardianEntity.PartitionKeyValue, RowKey = "maria@example.com", GuardianId = "maria", FullName = "Maria Rivera", Email = "maria@example.com", CreatedUtc = When });
            db.GuardianScoutLinks.Add(new GuardianScoutLinkEntity { PartitionKey = "maria", RowKey = "alex", GuardianId = "maria", ScoutId = "alex", ScoutName = "Alex Rivera", CreatedUtc = When });
            await db.SaveChangesAsync();
        }
        var date = (await service.RunBackupAsync()).Manifest!.Date;
        var backups = _backups.Store;

        // A local database that already has its own scout and none of the live roster.
        await sql.ResetAsync();
        await new ScoutRepository(sql.Db, TimeProvider.System).AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "local", FullName = "Local Scout", CachedBalance = 12m });

        var first = await RosterImportCommand.ImportAsync(backups, date, sql.NewContext, When);
        var again = await RosterImportCommand.ImportAsync(backups, date, sql.NewContext, When);

        Assert.Equal(new RosterImportResult(1, 1, 1, 1, 1, 1), first);
        Assert.Equal(new RosterImportResult(0, 1, 0, 1, 0, 1), again);
        await using var check = sql.NewContext();
        var alex = await check.Scouts.SingleAsync(s => s.RowKey == "alex");
        Assert.Equal("Alex Rivera", alex.FullName);
        Assert.Equal(0m, alex.CachedBalance);                                    // was 50.25 on the live side
        Assert.Equal(12m, (await check.Scouts.SingleAsync(s => s.RowKey == "local")).CachedBalance);
        Assert.Equal("maria", (await check.Guardians.SingleAsync()).GuardianId);
        Assert.Equal("alex", (await check.GuardianScoutLinks.SingleAsync()).ScoutId);
        Assert.Equal(0, await check.Invoices.CountAsync());
        Assert.Equal(0, await check.ReimbursementRequests.CountAsync());
    }

    [SkippableFact]
    public async Task A_pre_sql_snapshot_is_refused()
    {
        var (service, _) = await ReadyAsync();
        var date = (await service.RunBackupAsync()).Manifest!.Date;
        var manifestPath = $"{date}/manifest.json";
        var manifest = await TestBlobStores.ReadAllAsync(_backups.Store, BackupService.BackupsContainer, manifestPath);
        var oldStyle = Encoding.UTF8.GetString(manifest!).Replace("\"Format\": \"sql-1\"", "\"Format\": \"tables-1\"");
        using (var content = new MemoryStream(Encoding.UTF8.GetBytes(oldStyle)))
        {
            await _backups.Store.UploadAsync(BackupService.BackupsContainer, manifestPath, content, "application/json");
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(date, true, _ => { }));

        Assert.Contains("before the move to SQL", ex.Message);
    }
}
