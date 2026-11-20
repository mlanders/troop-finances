namespace BsaTroopFinances.Services;

/// <summary>What a completed snapshot contains. Written last, so its presence marks the snapshot complete.</summary>
public record BackupManifest(
    string Date,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    Dictionary<string, int> TableRowCounts,
    int ReceiptCount,
    long ReceiptBytes,
    string AppVersion,
    string Format = "tables-1")
{
    public int TotalRows => TableRowCounts.Values.Sum();
}

public record BackupRunResult(bool Succeeded, string Message, BackupManifest? Manifest = null);

public interface IBackupService
{
    /// <summary>False when no backup store is configured (ConnectionStrings:BackupStorage or
    /// BackupStorage:Provider); every other call is then a no-op.</summary>
    bool IsConfigured { get; }

    /// <summary>Today's snapshot date (Pacific), the folder name under backups/.</summary>
    string TodaySnapshotDate { get; }

    Task<bool> IsSnapshotCompleteAsync(string date, CancellationToken cancellationToken = default);

    /// <summary>Backs up today's snapshot (unless already complete), then prunes to the newest 14.</summary>
    Task<BackupRunResult> RunBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Completed snapshots, newest first.</summary>
    Task<IReadOnlyList<BackupManifest>> ListBackupsAsync(CancellationToken cancellationToken = default);

    Task<BackupManifest?> GetManifestAsync(string date, CancellationToken cancellationToken = default);

    /// <summary>Writes a snapshot back into the SQL database (one transaction) and the receipts container.</summary>
    Task RestoreAsync(string date, bool replaceAll, Action<string> progress, CancellationToken cancellationToken = default);
}
