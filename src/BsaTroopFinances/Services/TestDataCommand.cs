using Azure.Storage.Blobs;
using BsaTroopFinances.Data;
using BsaTroopFinances.Storage;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Services;

/// <summary>
/// Command-line mode that fills a local database with a year of test activity for the scouts and
/// guardians already in it (see <see cref="TestDataGenerator"/>):
///   dotnet run --project src/BsaTroopFinances -- seed-test-data [--invoices 250] [--reimbursements 120]
///                                                               [--fundraisers 2] [--seed 441] [--yes]
/// It refuses to run outside the Development environment, or against a database or storage
/// account that isn't on this machine, unless those are named with --target &lt;SQL connection
/// string&gt; and --receipts-target &lt;storage connection string or blob service URL&gt; -- meant
/// only for a hosted site that holds nothing but test data. That run can't be skipped past with
/// --yes: the database name has to be typed. No emails are sent or logged.
/// </summary>
public static class TestDataCommand
{
    public const string Name = "seed-test-data";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, IHostEnvironment environment)
    {
        int Option(string name, int fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) && value >= 0 ? value : fallback;
        }

        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine($"{Name} only runs in the Development environment (this is {environment.EnvironmentName}).");
            return 2;
        }

        string? TextOption(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        // The configured database and storage must be on this machine. Anything else has to be
        // named on the command line, and is confirmed by typing the database's name.
        var explicitTarget = TextOption("--target") is not null || TextOption("--receipts-target") is not null;
        var database = FinanceDatabase.Connection(configuration, TextOption("--target"));
        var storageConnection = TextOption("--receipts-target") ?? configuration.GetConnectionString("AzureStorage");
        if (database is null || string.IsNullOrWhiteSpace(storageConnection))
        {
            Console.Error.WriteLine("Needs a database (--target or ConnectionStrings:FinanceDb) and receipt storage (--receipts-target or ConnectionStrings:AzureStorage).");
            return 2;
        }

        // --receipts-target takes a connection string, or a blob service URL to sign in through az login.
        var blobs = storageConnection.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new BlobServiceClient(new Uri(storageConnection), new Azure.Identity.DefaultAzureCredential())
            : new BlobServiceClient(storageConnection);
        var remote = !database.IsOnThisMachine || !blobs.Uri.IsLoopback;
        if (database.IsOnThisMachine != blobs.Uri.IsLoopback)
        {
            Console.Error.WriteLine($"The database ('{database.Server}') and receipt storage ('{blobs.Uri.Host}') must both be on this machine or both be hosted, so receipts land where that database's site reads them.");
            return 2;
        }
        if (remote && !explicitTarget)
        {
            Console.Error.WriteLine($"{Name} only writes to a database and storage account on this machine, not '{database.Server}' / '{blobs.Uri.Host}', unless they are named with --target and --receipts-target.");
            return 2;
        }

        var defaults = new TestDataOptions();
        var options = new TestDataOptions(
            Option("--invoices", defaults.Invoices), Option("--reimbursements", defaults.Reimbursements),
            Option("--fundraisers", defaults.Fundraisers), Option("--seed", defaults.Seed));

        var factory = new FinanceDatabase.Factory(database);
        int scouts, guardians;
        await using (var db = factory.CreateDbContext())
        {
            // A hosted database is kept up to date by its own app; only a local one is created here.
            if (!remote)
            {
                await db.Database.MigrateAsync();
            }

            scouts = await db.Scouts.CountAsync(s => s.IsActive);
            guardians = await db.Guardians.CountAsync(g => g.IsActive);
        }

        Console.WriteLine($"Database: '{database.DatabaseName}' on '{database.Server}' ({database.Provider}, {scouts} active scouts, {guardians} active guardians)");
        Console.WriteLine($"Adding:   {options.Invoices} invoices, {options.Reimbursements} reimbursements and {options.Fundraisers} fundraisers across the last 12 months (seed {options.Seed}).");
        Console.WriteLine("          Existing records are left alone; scout balances change with the new activity.");

        if (remote)
        {
            Console.WriteLine($"Receipts: storage account '{blobs.AccountName}'");
            Console.WriteLine("THIS IS NOT A LOCAL DATABASE. The test records can't be removed afterwards except by restoring a backup.");
            Console.Write($"Type the database name ({database.DatabaseName}) to continue: ");
            if (Console.ReadLine()?.Trim() != database.DatabaseName)
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
        }
        else if (!args.Contains("--yes"))
        {
            Console.Write("Type SEED to continue: ");
            if (Console.ReadLine()?.Trim() != "SEED")
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
        }

        TestDataSummary summary;
        try
        {
            summary = await TestDataLoader.GenerateAsync(factory, new AzureBlobStore(blobs), options);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is Azure.RequestFailedException or AggregateException)
        {
            Console.Error.WriteLine("Receipt storage isn't reachable, so reimbursements can't be created. Start Azurite, or pass --reimbursements 0.");
            return 1;
        }

        Console.WriteLine($"Fundraisers:    {summary.Fundraisers} ({summary.Allocations} allocations to scouts)");
        Console.WriteLine($"Invoices:       {summary.Invoices} ({summary.InvoicePayments} payments recorded, {summary.InvoicesVoided} voided)");
        Console.WriteLine($"Reimbursements: {summary.Reimbursements} ({summary.ReimbursementsApproved} approved, {summary.ReimbursementsPaid} of those paid, {summary.ReimbursementsRejected} rejected)");
        Console.WriteLine($"Everything was recorded by \"{TestDataGenerator.CreatedBy}\".");
        return 0;
    }
}
