using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BsaTroopFinances.Services;

public record RosterImportResult(int ScoutsAdded, int ScoutsInBackup, int GuardiansAdded, int GuardiansInBackup, int LinksAdded, int LinksInBackup);

/// <summary>
/// Command-line mode that copies just the roster -- scouts, guardians and the links between them --
/// from a nightly backup of the live site into the local database, so test data (see
/// <see cref="TestDataCommand"/>) can be generated for the real troop:
///   $env:ConnectionStrings__BackupStorage = "&lt;backup account connection string&gt;"
///   dotnet run --project src/BsaTroopFinances -- import-roster [&lt;yyyy-MM-dd&gt;] [--yes]
/// Uses the newest backup unless a date is given. Only reads from the backup account. No money
/// comes across: scouts arrive with a zero balance and no invoices, ledger, reimbursements or
/// receipts. Rows already in the local database are left as they are. Local development only: it
/// refuses to run outside the Development environment or against a database that isn't on this
/// machine.
/// </summary>
public static class RosterImportCommand
{
    public const string Name = "import-roster";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine($"{Name} only runs in the Development environment (this is {environment.EnvironmentName}).");
            return 2;
        }

        var database = FinanceDatabase.Connection(configuration);
        var backupStore = BlobStores.CreateBackup(configuration);
        if (database is null || backupStore is null)
        {
            Console.Error.WriteLine("Needs ConnectionStrings:FinanceDb and a backup store (ConnectionStrings:BackupStorage or BackupStorage:Provider).");
            return 2;
        }

        if (!database.IsOnThisMachine)
        {
            Console.Error.WriteLine($"{Name} only writes to a database on this machine, not '{database.Server}'.");
            return 2;
        }

        var factory = new FinanceDatabase.Factory(database);
        var backups = new BackupService(factory, backupStore, backupStore, TimeProvider.System, NullLogger<BackupService>.Instance);

        var date = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        var manifest = date is null
            ? (await backups.ListBackupsAsync()).FirstOrDefault(m => m.Format == BackupService.SqlFormat)
            : await backups.GetManifestAsync(date);
        if (manifest is null || manifest.Format != BackupService.SqlFormat)
        {
            Console.Error.WriteLine(date is null ? "No backup was found." : $"No backup in the current format exists for {date}.");
            return 1;
        }

        await using (var db = factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        Console.WriteLine($"From: backup {manifest.Date} in {backupStore.Description} (read only)");
        Console.WriteLine($"To:   database '{database.DatabaseName}' on '{database.Server}' ({database.Provider})");
        Console.WriteLine("Adds the scouts, guardians and links that aren't here yet, with zero balances. Nothing else is copied or changed.");
        if (!args.Contains("--yes"))
        {
            Console.Write("Type IMPORT to continue: ");
            if (Console.ReadLine()?.Trim() != "IMPORT")
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
        }

        var result = await ImportAsync(backupStore, manifest.Date,
            factory.CreateDbContext, DateTimeOffset.UtcNow);
        Console.WriteLine($"Scouts:    added {result.ScoutsAdded} of {result.ScoutsInBackup}");
        Console.WriteLine($"Guardians: added {result.GuardiansAdded} of {result.GuardiansInBackup}");
        Console.WriteLine($"Links:     added {result.LinksAdded} of {result.LinksInBackup}");
        return 0;
    }

    /// <summary>Adds the backed-up roster rows the database doesn't already have, in one transaction.</summary>
    public static async Task<RosterImportResult> ImportAsync(IBlobStore backups, string date, Func<FinanceDbContext> newContext, DateTimeOffset now)
    {
        var scouts = await ReadTableAsync<ScoutEntity>(backups, date, TableNames.Scouts);
        var guardians = await ReadTableAsync<GuardianEntity>(backups, date, TableNames.Guardians);
        var links = await ReadTableAsync<GuardianScoutLinkEntity>(backups, date, TableNames.GuardianScoutLinks);

        await using var db = newContext();
        var haveScouts = (await db.Scouts.Select(s => s.RowKey).ToListAsync()).ToHashSet();
        var haveEmails = (await db.Guardians.Select(g => g.RowKey).ToListAsync()).ToHashSet();
        var haveGuardianIds = (await db.Guardians.Select(g => g.GuardianId).ToListAsync()).ToHashSet();
        var haveLinks = (await db.GuardianScoutLinks.Select(l => new { l.PartitionKey, l.RowKey }).ToListAsync())
            .Select(l => (l.PartitionKey, l.RowKey)).ToHashSet();

        var newScouts = scouts.Where(s => !haveScouts.Contains(s.RowKey)).ToList();
        foreach (var scout in newScouts)
        {
            // The ledger that explains a balance stays behind, so the balance does too.
            scout.CachedBalance = 0;
            scout.BalanceUpdatedUtc = now;
        }

        var newGuardians = guardians.Where(g => !haveEmails.Contains(g.RowKey) && !haveGuardianIds.Contains(g.GuardianId)).ToList();

        // A link only makes sense when both ends are here under the same ids as on the live site.
        var scoutIds = haveScouts.Concat(newScouts.Select(s => s.RowKey)).ToHashSet();
        var guardianIds = haveGuardianIds.Concat(newGuardians.Select(g => g.GuardianId)).ToHashSet();
        var newLinks = links
            .Where(l => !haveLinks.Contains((l.PartitionKey, l.RowKey)) && scoutIds.Contains(l.ScoutId) && guardianIds.Contains(l.GuardianId))
            .ToList();

        db.AddRange(newScouts);
        db.AddRange(newGuardians);
        db.AddRange(newLinks);
        await db.SaveChangesAsync();
        return new RosterImportResult(newScouts.Count, scouts.Count, newGuardians.Count, guardians.Count, newLinks.Count, links.Count);
    }

    private static async Task<List<T>> ReadTableAsync<T>(IBlobStore backups, string date, string table)
    {
        var rows = new List<T>();
        var blob = await backups.OpenReadAsync(BackupService.BackupsContainer, $"{date}/sql/{table}.jsonl.gz")
            ?? throw new FileNotFoundException($"Backup {date} has no {table} table.");
        await using var stream = blob.Content;
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                rows.Add(JsonSerializer.Deserialize<T>(line)!);
            }
        }

        return rows;
    }
}
