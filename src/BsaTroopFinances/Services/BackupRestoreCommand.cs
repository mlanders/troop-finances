using Azure.Storage.Blobs;
using BsaTroopFinances.Data;
using BsaTroopFinances.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace BsaTroopFinances.Services;

/// <summary>
/// Command-line restore, run by hand from a machine with the connection strings (see
/// infra/DEPLOY.md "Backups and restore"):
///   dotnet BsaTroopFinances.dll restore-backup &lt;yyyy-MM-dd&gt; [--target &lt;database connection string&gt;]
///                                              [--receipts-target &lt;Azure storage connection string&gt;] [--replace-all]
/// Reads the backup store, and (unless overridden) ConnectionStrings:FinanceDb for the database and
/// the receipt store, from the usual app configuration (see Storage/BlobStores). Database:Provider
/// says which engine the database is, --target included, so a backup taken on one engine can be
/// restored into the other (that's how a site moves between SQL Server and PostgreSQL).
/// Shows the snapshot and asks for the date to be typed back before writing anything. The rows
/// are restored in one transaction.
/// </summary>
public static class BackupRestoreCommand
{
    public const string Name = "restore-backup";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Usage: {Name} <yyyy-MM-dd> [--target <database connection string>] [--receipts-target <storage connection string>] [--replace-all]");
            return 2;
        }

        string? Option(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var date = args[0];
        var replaceAll = args.Contains("--replace-all");
        var target = FinanceDatabase.Connection(configuration, Option("--target"));
        var receiptsTarget = Option("--receipts-target");
        var backups = BlobStores.CreateBackup(configuration);

        if (target is null || backups is null)
        {
            Console.Error.WriteLine("Needs a backup store (ConnectionStrings:BackupStorage or BackupStorage:Provider) and a database (--target or ConnectionStrings:FinanceDb, with Database:Provider for PostgreSQL).");
            return 2;
        }

        var receipts = receiptsTarget is null ? BlobStores.CreateMain(configuration) : new AzureBlobStore(new BlobServiceClient(receiptsTarget));
        var service = new BackupService(new FinanceDatabase.Factory(target), receipts,
            backups, TimeProvider.System, NullLogger<BackupService>.Instance);

        var manifest = await service.GetManifestAsync(date);
        if (manifest is null)
        {
            Console.Error.WriteLine($"No complete backup exists for {date}. Available:");
            foreach (var available in await service.ListBackupsAsync())
            {
                Console.Error.WriteLine($"  {available.Date}{(available.Format == BackupService.SqlFormat ? "" : "  (pre-SQL: can't be restored)")}");
            }

            return 1;
        }

        Console.WriteLine($"Backup {manifest.Date}, finished {manifest.FinishedUtc.ToLocalTime():MMM d, yyyy h:mm tt}");
        foreach (var (table, rows) in manifest.TableRowCounts)
        {
            Console.WriteLine($"  {table,-26} {rows,8} rows");
        }

        Console.WriteLine($"  {"receipts",-26} {manifest.ReceiptCount,8} files");
        Console.WriteLine();
        Console.WriteLine($"Target database: '{target.DatabaseName}' on '{target.Server}' ({target.Provider})");
        Console.WriteLine($"Target receipts: {receipts.Description}");
        Console.WriteLine($"Backups read from: {backups.Description}");
        Console.WriteLine(replaceAll
            ? "Mode: --replace-all -- every row in these tables is DELETED first, then the backup is written."
            : "Mode: upsert -- backed-up rows overwrite matching rows; rows not in the backup are left alone.");
        Console.Write($"Type the backup date ({date}) to continue: ");
        if (Console.ReadLine()?.Trim() != date)
        {
            Console.WriteLine("Cancelled.");
            return 1;
        }

        await service.RestoreAsync(date, replaceAll, Console.WriteLine);
        Console.WriteLine("Restore complete.");
        return 0;
    }
}
