using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BsaTroopFinances.Data;
using BsaTroopFinances.Storage;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Services;

/// <summary>
/// Nightly snapshots of every table in <see cref="TableNames.Records"/> plus the receipts
/// container, written to a separate store (ConnectionStrings:BackupStorage, or BackupStorage:Provider
/// -- see Storage/BlobStores) in its "backups" container under {yyyy-MM-dd}/ (Pacific date):
///   sql/{Table}.jsonl.gz     one JSON line per row, the entity exactly as stored in SQL
///   receipts/{path}          a copy of each receipt blob
///   manifest.json            written last -- a snapshot without one is incomplete
/// Snapshots taken before the move to SQL hold tables/{Table}.jsonl.gz in the old Table Storage
/// format instead (manifest Format "tables-1"); RestoreAsync refuses them, since the Table Storage
/// import that could read them has been removed.
/// Retention is snapshot-count based (<see cref="SnapshotsToKeep"/>), pruned only after a
/// successful backup, so a run of failed nights never ages out the last good copies. The backup
/// account's 60-day lifecycle rule (infra/main.bicep) is only a backstop for that. Azure SQL's own
/// point-in-time restore is a second, independent safety net.
/// </summary>
public class BackupService : IBackupService
{
    public const string BackupsContainer = "backups";
    public const int SnapshotsToKeep = 14;
    public const string SqlFormat = "sql-1";
    public const string NotConfiguredMessage = "Backups aren't configured (set ConnectionStrings:BackupStorage or BackupStorage:Provider).";
    private static readonly TimeSpan IncompleteSnapshotMaxAge = TimeSpan.FromDays(2);
    private const int RestoreBatchSize = 500;

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    // Rows are written as the entity's own properties, minus the SQL row version (the database
    // assigns a fresh one on restore).
    private static readonly JsonSerializerOptions RowJson = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    foreach (var property in info.Properties.Where(p => p.Name == "Version").ToList())
                    {
                        info.Properties.Remove(property);
                    }
                },
            },
        },
    };

    private readonly IDbContextFactory<FinanceDbContext> _dbFactory;
    private readonly IBlobStore _main;
    private readonly IBlobStore? _backups;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public BackupService(IDbContextFactory<FinanceDbContext> dbFactory, IBlobStore main, IBlobStore? backups,
        TimeProvider timeProvider, ILogger<BackupService> logger)
    {
        _dbFactory = dbFactory;
        _main = main;
        _backups = backups;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsConfigured => _backups is not null;

    public string TodaySnapshotDate => _timeProvider.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private IBlobStore Backups => _backups!;

    public async Task<bool> IsSnapshotCompleteAsync(string date, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        return await Backups.ExistsAsync(BackupsContainer, $"{date}/manifest.json", cancellationToken);
    }

    public async Task<BackupRunResult> RunBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new BackupRunResult(false, NotConfiguredMessage);
        }

        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            return new BackupRunResult(false, "A backup is already running.");
        }

        try
        {
            var date = TodaySnapshotDate;
            if (await IsSnapshotCompleteAsync(date, cancellationToken))
            {
                return new BackupRunResult(true, $"Today's backup ({date}) is already complete.", await GetManifestAsync(date, cancellationToken));
            }

            var manifest = await WriteSnapshotAsync(date, cancellationToken);
            _logger.LogInformation("Backup {Date} complete: {Rows} rows, {Receipts} receipts", date, manifest.TotalRows, manifest.ReceiptCount);

            await PruneAsync(cancellationToken);
            return new BackupRunResult(true, $"Backup {date} complete.", manifest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Backup failed");
            return new BackupRunResult(false, "The backup failed. Details are in the app log.");
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<BackupManifest> WriteSnapshotAsync(string date, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetUtcNow();
        await Backups.EnsureContainerAsync(BackupsContainer, cancellationToken);

        // Clear leftovers from an earlier failed attempt today, so the snapshot is exactly one run.
        await DeletePrefixAsync($"{date}/", cancellationToken);

        var rowCounts = new Dictionary<string, int>();
        foreach (var (table, entity) in TableNames.Records)
        {
            rowCounts[table] = await (Task<int>)Generic(nameof(ExportTableAsync), entity)
                .Invoke(this, [$"{date}/sql/{table}.jsonl.gz", cancellationToken])!;
        }

        var (receiptCount, receiptBytes) = await CopyBlobsAsync(_main, TableNames.ReceiptsContainer, sourcePrefix: null,
            Backups, BackupsContainer, destinationPrefix: $"{date}/receipts/", cancellationToken);

        var manifest = new BackupManifest(date, started, _timeProvider.GetUtcNow(), rowCounts, receiptCount, receiptBytes,
            typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown", SqlFormat);
        using var manifestContent = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson));
        await Backups.UploadAsync(BackupsContainer, $"{date}/manifest.json", manifestContent, "application/json", cancellationToken);
        return manifest;
    }

    private async Task<int> ExportTableAsync<T>(string blobPath, CancellationToken cancellationToken) where T : class
    {
        var count = 0;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using (var blobStream = await Backups.OpenWriteAsync(BackupsContainer, blobPath, "application/gzip", cancellationToken))
        await using (var gzip = new GZipStream(blobStream, CompressionLevel.Optimal))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            await foreach (var row in db.Set<T>().AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(row, RowJson));
                count++;
            }
        }

        return count;
    }

    /// <summary>Copies every file under a prefix from one store to another: inside Azure when both
    /// are Azure accounts and the source has an account key, streamed through this app otherwise.</summary>
    private async Task<(int Count, long Bytes)> CopyBlobsAsync(IBlobStore source, string sourceContainer, string? sourcePrefix,
        IBlobStore destination, string destinationContainer, string destinationPrefix, CancellationToken cancellationToken)
    {
        if (!await source.ContainerExistsAsync(sourceContainer, cancellationToken))
        {
            return (0, 0);
        }

        await destination.EnsureContainerAsync(destinationContainer, cancellationToken);
        var count = 0;
        long bytes = 0;
        await foreach (var item in source.ListAsync(sourceContainer, sourcePrefix, cancellationToken))
        {
            var relativePath = sourcePrefix is null ? item.Path : item.Path[sourcePrefix.Length..];
            await BlobStores.CopyAsync(source, sourceContainer, item.Path, destination, destinationContainer,
                destinationPrefix + relativePath, _timeProvider.GetUtcNow(), cancellationToken);
            count++;
            bytes += item.Length;
        }

        return (count, bytes);
    }

    /// <summary>Keeps the newest <see cref="SnapshotsToKeep"/> complete snapshots; also removes
    /// incomplete ones (failed runs) older than two days. Only called after a successful backup.</summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var complete = new List<string>();
        var incomplete = new List<string>();
        foreach (var date in await ListSnapshotDatesAsync(cancellationToken))
        {
            (await IsSnapshotCompleteAsync(date, cancellationToken) ? complete : incomplete).Add(date);
        }

        var cutoff = _timeProvider.GetLocalNow().Date - IncompleteSnapshotMaxAge;
        var toDelete = complete.OrderByDescending(d => d, StringComparer.Ordinal).Skip(SnapshotsToKeep)
            .Concat(incomplete.Where(d => DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed < cutoff))
            .ToList();

        foreach (var date in toDelete)
        {
            await DeletePrefixAsync($"{date}/", cancellationToken);
            _logger.LogInformation("Pruned backup snapshot {Date}", date);
        }
    }

    private async Task<List<string>> ListSnapshotDatesAsync(CancellationToken cancellationToken)
    {
        var dates = new List<string>();
        if (!await Backups.ContainerExistsAsync(BackupsContainer, cancellationToken))
        {
            return dates;
        }

        await foreach (var folder in Backups.ListFoldersAsync(BackupsContainer, cancellationToken))
        {
            dates.Add(folder);
        }

        return dates;
    }

    private async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        // Listed in full first: not every store copes with deleting while it's being listed.
        var paths = new List<string>();
        await foreach (var item in Backups.ListAsync(BackupsContainer, prefix, cancellationToken))
        {
            paths.Add(item.Path);
        }

        foreach (var path in paths)
        {
            await Backups.DeleteAsync(BackupsContainer, path, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<BackupManifest>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var manifests = new List<BackupManifest>();
        foreach (var date in await ListSnapshotDatesAsync(cancellationToken))
        {
            var manifest = await GetManifestAsync(date, cancellationToken);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests.OrderByDescending(m => m.Date, StringComparer.Ordinal).ToList();
    }

    public async Task<BackupManifest?> GetManifestAsync(string date, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var blob = await Backups.OpenReadAsync(BackupsContainer, $"{date}/manifest.json", cancellationToken);
        if (blob is null)
        {
            return null;
        }

        await using (blob.Content)
        {
            return await JsonSerializer.DeserializeAsync<BackupManifest>(blob.Content, cancellationToken: cancellationToken);
        }
    }

    public async Task RestoreAsync(string date, bool replaceAll, Action<string> progress, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(NotConfiguredMessage);
        }

        var manifest = await GetManifestAsync(date, cancellationToken)
            ?? throw new InvalidOperationException($"No complete backup exists for {date}.");
        if (manifest.Format != SqlFormat)
        {
            throw new InvalidOperationException(
                $"Backup {date} was taken before the move to SQL and can't be restored.");
        }

        // The whole restore is one transaction: it lands completely or not at all.
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var (table, entity) in TableNames.Records)
            {
                var restored = await (Task<int>)Generic(nameof(RestoreTableAsync), entity)
                    .Invoke(this, [db, $"{date}/sql/{table}.jsonl.gz", replaceAll, cancellationToken])!;
                progress($"{table}: restored {restored} rows");
            }
            await transaction.CommitAsync(cancellationToken);
        });

        var (receipts, _) = await CopyBlobsAsync(Backups, BackupsContainer, sourcePrefix: $"{date}/receipts/",
            _main, TableNames.ReceiptsContainer, destinationPrefix: string.Empty, cancellationToken);
        progress($"receipts: restored {receipts} files");
    }

    private async Task<int> RestoreTableAsync<T>(FinanceDbContext db, string blobPath, bool replaceAll, CancellationToken cancellationToken) where T : class
    {
        if (replaceAll)
        {
            await db.Set<T>().ExecuteDeleteAsync(cancellationToken);
        }

        var blob = await Backups.OpenReadAsync(BackupsContainer, blobPath, cancellationToken);
        if (blob is null)
        {
            return 0;
        }

        var restored = 0;
        await using var stream = blob.Content;
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = JsonSerializer.Deserialize<T>(line, RowJson)!;
            var entry = db.Entry(row);
            var partitionKey = (string)entry.Property("PartitionKey").CurrentValue!;
            var rowKey = (string)entry.Property("RowKey").CurrentValue!;
            var exists = !replaceAll && await db.Set<T>().AnyAsync(
                e => EF.Property<string>(e, "PartitionKey") == partitionKey && EF.Property<string>(e, "RowKey") == rowKey, cancellationToken);
            if (exists)
            {
                // Overwrite the current row with the backed-up one.
                await db.UpdateDetachedAsync(row);
            }
            else
            {
                db.Add(row);
            }

            if (++restored % RestoreBatchSize == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return restored;
    }

    private static MethodInfo Generic(string name, Type entity) =>
        typeof(BackupService).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(entity);
}
