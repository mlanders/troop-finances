using System.Text;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>
/// Fills an empty local database with a small, realistic troop so every admin page has something
/// to show: scouts with balances, linked guardians, a fundraiser with allocations, invoices in each
/// status, and reimbursements in each status. Runs at startup only with DevAuth on (so only in
/// Development) and DevAuth:SeedSampleData=true, and only when there are no scouts yet. Everything
/// goes through the app's real services, so ledgers, balances, indexes and audit history are
/// consistent exactly as if a treasurer had done it.
/// </summary>
public class DevSampleDataSeeder(IServiceScopeFactory scopeFactory, ILogger<DevSampleDataSeeder> logger) : IHostedService
{
    private const string Treasurer = "Dev Writer";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var scouts = services.GetRequiredService<IScoutRepository>();
        if ((await scouts.GetAllAsync()).Count > 0)
        {
            logger.LogInformation("Sample data skipped: scouts already exist");
            return;
        }

        var guardians = services.GetRequiredService<IGuardianRepository>();
        var links = services.GetRequiredService<IGuardianScoutLinkRepository>();
        var fundraisers = services.GetRequiredService<IFundraiserEventRepository>();
        var accounts = services.GetRequiredService<IScoutAccountService>();
        var donations = services.GetRequiredService<IDonationService>();
        var reimbursements = services.GetRequiredService<IReimbursementService>();
        var now = DateTimeOffset.UtcNow;

        var scoutNames = new[] { "Alex Rivera", "Jordan Lee", "Sam Patel", "Casey Morgan" };
        var scoutIds = new List<string>();
        foreach (var name in scoutNames)
        {
            var scout = new ScoutEntity
            {
                PartitionKey = ScoutEntity.PartitionKeyValue,
                RowKey = ScoutEntity.NewRowKey(),
                FullName = name,
                IsActive = true,
                CreatedUtc = now,
                CachedBalance = 0,
                BalanceUpdatedUtc = now,
            };
            await scouts.AddAsync(scout);
            scoutIds.Add(scout.ScoutId);
        }

        var parentNames = new[] { "Maria Rivera", "Chris Lee", "Priya Patel" };
        var parentIds = new List<string>();
        for (var i = 0; i < parentNames.Length; i++)
        {
            var email = $"{parentNames[i].Split(' ')[0].ToLowerInvariant()}@example.com";
            var guardian = new GuardianEntity
            {
                PartitionKey = GuardianEntity.PartitionKeyValue,
                RowKey = GuardianEntity.NormalizeEmail(email),
                GuardianId = Guid.NewGuid().ToString("N"),
                FullName = parentNames[i],
                Email = email,
                IsActive = true,
                CreatedUtc = now,
            };
            await guardians.AddAsync(guardian);
            parentIds.Add(guardian.GuardianId);
            await links.AddLinkAsync(new GuardianScoutLinkEntity
            {
                PartitionKey = GuardianScoutLinkEntity.BuildPartitionKey(guardian.GuardianId),
                RowKey = GuardianScoutLinkEntity.BuildRowKey(scoutIds[i]),
                GuardianId = guardian.GuardianId,
                ScoutId = scoutIds[i],
                ScoutName = scoutNames[i],
                CreatedUtc = now,
            });
        }

        // A family with two scouts, so a family payment can cover siblings' invoices.
        await links.AddLinkAsync(new GuardianScoutLinkEntity
        {
            PartitionKey = GuardianScoutLinkEntity.BuildPartitionKey(parentIds[0]),
            RowKey = GuardianScoutLinkEntity.BuildRowKey(scoutIds[3]),
            GuardianId = parentIds[0],
            ScoutId = scoutIds[3],
            ScoutName = scoutNames[3],
            CreatedUtc = now,
        });

        var popcorn = new FundraiserEventEntity
        {
            PartitionKey = FundraiserEventEntity.PartitionKeyValue,
            RowKey = FundraiserEventEntity.NewRowKey(),
            Name = "Fall Popcorn Sale",
            EventDate = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-20), TimeSpan.Zero),
            Description = "Storefront and take-order popcorn",
            TotalRaised = 600,
            TotalExpenses = 120,
            GoalAmount = 1000,
            StatusValue = FundraiserEventStatus.Open,
            CreatedUtc = now,
            CreatedBy = Treasurer,
        };
        await fundraisers.AddAsync(popcorn);
        await accounts.ApplyFundraiserAllocationAsync(scoutIds[0], popcorn.EventId, 150, "Popcorn share", Treasurer, hoursWorked: 6);
        await accounts.ApplyFundraiserAllocationAsync(scoutIds[1], popcorn.EventId, 100, "Popcorn share", Treasurer, hoursWorked: 4);
        await accounts.ApplyFundraiserAllocationAsync(scoutIds[2], popcorn.EventId, 50, "Popcorn share", Treasurer, hoursWorked: 2);

        // An unrestricted check still owed a thank-you, a camp scholarship credited to one scout,
        // and donated tents.
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        await donations.RecordAsync(new DonationEntity
        {
            DonorName = "Main Street Hardware", DonorEmail = "owner@example.com", ReceivedDate = today.AddDays(-12),
            Amount = 300, Method = InvoicePaymentMethod.Check, Reference = "5521",
        }, Treasurer);
        var scholarship = await donations.RecordAsync(new DonationEntity
        {
            DonorName = "The Nguyen family", IsAnonymous = true, ReceivedDate = today.AddDays(-5),
            Amount = 200, Method = InvoicePaymentMethod.Venmo, Reference = "@nguyen-fam",
            IsRestricted = true, Purpose = "Summer camp scholarship",
        }, Treasurer);
        await donations.CreditScoutsAsync(scholarship.DonationId, [new(scoutIds[3], 200)], string.Empty, Treasurer);
        await donations.RecordAsync(new DonationEntity
        {
            DonorName = "Pat Morgan", ReceivedDate = today.AddDays(-30), Amount = 180,
            Method = DonationMethod.InKind, Reference = "Two 4-person tents",
        }, Treasurer);

        var due = now.AddDays(21);
        // Outstanding, part covered by the scout's account.
        await accounts.IssueInvoiceAsync(scoutIds[0], 275, 100, due, "Summer camp deposit", Treasurer);
        // Outstanding, nothing applied.
        await accounts.IssueInvoiceAsync(scoutIds[3], 45, 0, due, "Campout food", Treasurer);
        // Second outstanding invoice for the Rivera family's other scout.
        await accounts.IssueInvoiceAsync(scoutIds[3], 18, 0, now.AddDays(10), "Patrol flag", Treasurer);
        // Paid in full by the scout's account.
        await accounts.IssueInvoiceAsync(scoutIds[1], 40, 40, due, "Merit badge fair", Treasurer);
        // Paid by check.
        var paidByCheck = await accounts.IssueInvoiceAsync(scoutIds[2], 60, 0, due, "Troop T-shirt", Treasurer);
        await accounts.RecordInvoicePaymentAsync(scoutIds[2], paidByCheck.InvoiceId, 60, InvoicePaymentMethod.Check, "Check 1042", Treasurer);
        // Voided.
        var voided = await accounts.IssueInvoiceAsync(scoutIds[3], 30, 0, due, "Duplicate charge", Treasurer);
        await accounts.VoidInvoiceAsync(scoutIds[3], voided.InvoiceId, "Entered twice", Treasurer);

        // The troop's own money: checking and Venmo, opened two months back.
        var bank = services.GetRequiredService<ITroopAccountService>();
        DateOnly Day(int daysAgo) => DateOnly.FromDateTime(today.AddDays(-daysAgo).UtcDateTime);
        var checking = await bank.AddAccountAsync("Troop checking", TroopAccountKind.Bank, 2500, Day(60), Treasurer);
        var venmo = await bank.AddAccountAsync("Venmo", TroopAccountKind.Venmo, 0, Day(60), Treasurer);

        async Task<ReimbursementRequestEntity> SubmitAsync(int parent, decimal amount, string description)
        {
            var receipt = Encoding.ASCII.GetBytes("%PDF-1.4\n% sample receipt\n");
            using var stream = new MemoryStream(receipt);
            return await reimbursements.SubmitAsync(parentIds[parent], parentNames[parent], $"{parentNames[parent].Split(' ')[0].ToLowerInvariant()}@example.com",
                scoutIds[parent], scoutNames[parent], amount, description, stream, "application/pdf", "receipt.pdf", receipt.Length);
        }

        await SubmitAsync(0, 42.18m, "Campout groceries");
        var approved = await SubmitAsync(1, 19.99m, "First aid kit refills");
        await reimbursements.ApproveAsync(parentIds[1], approved.RequestId, Treasurer, null);
        var paid = await SubmitAsync(2, 64.50m, "Gas for troop trailer");
        await reimbursements.ApproveAsync(parentIds[2], paid.RequestId, Treasurer, null);
        await bank.PayReimbursementAsync(parentIds[2], paid.RequestId, checking.AccountId, Day(3), "2001", Treasurer);
        var rejected = await SubmitAsync(0, 12.00m, "Personal snacks");
        await reimbursements.RejectAsync(parentIds[0], rejected.RequestId, Treasurer, "Not a troop expense");

        // The T-shirt check deposited and the Venmo gift moved to the bank; the hardware store's
        // check is still waiting to be deposited.
        var waiting = await bank.GetUndepositedAsync();
        var noCash = new Dictionary<decimal, int>();
        await bank.CreateDepositAsync(new DepositRequest(checking.AccountId, Day(2),
            waiting.Where(i => i.Kind == DepositLineKind.InvoicePayment).Select(i => (i.Kind, i.SourceId)).ToList(), [], noCash, "", ""), Treasurer);
        await bank.CreateDepositAsync(new DepositRequest(venmo.AccountId, Day(5),
            waiting.Where(i => i.Method == InvoicePaymentMethod.Venmo).Select(i => (i.Kind, i.SourceId)).ToList(), [], noCash, "", ""), Treasurer);
        await bank.TransferAsync(venmo.AccountId, checking.AccountId, Day(1), 200, "Venmo transfer to bank", Treasurer);
        await bank.RecordEntryAsync(checking.AccountId, TroopTransactionKind.Expense, Day(8), 86.40m, "Scout Shop",
            "Rank patches and merit badges", "1187", null, Treasurer);

        logger.LogInformation("Loaded sample data: {Scouts} scouts, {Guardians} guardians, 1 fundraiser, 6 invoices, 4 reimbursements",
            scoutNames.Length, parentNames.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
