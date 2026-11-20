using System.Text;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

/// <summary>A clock the test-data generator moves around, so the real services stamp each record
/// with the back-dated moment it is meant to have happened.</summary>
public sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public record TestDataOptions(int Invoices = 250, int Reimbursements = 120, int Fundraisers = 2, int Seed = 441);

public class TestDataSummary
{
    public int Fundraisers { get; set; }
    public int Allocations { get; set; }
    public int Invoices { get; set; }
    public int InvoicePayments { get; set; }
    public int InvoicesVoided { get; set; }
    public int Reimbursements { get; set; }
    public int ReimbursementsApproved { get; set; }
    public int ReimbursementsPaid { get; set; }
    public int ReimbursementsRejected { get; set; }
}

/// <summary>
/// Adds a large, realistic spread of invoices, reimbursements and fundraisers for the scouts and
/// guardians already in the database, back-dated across the last 12 months (see TestDataCommand).
/// Everything is planned on one timeline and played back oldest first through the app's real
/// services with the clock set to each moment, so ledgers, balances, statuses and audit history
/// come out exactly as if a treasurer had done it over the year. Every record is created by
/// <see cref="CreatedBy"/>, which is how to tell generated data from real data.
/// </summary>
public class TestDataGenerator(
    IScoutRepository scoutRepository,
    IGuardianRepository guardianRepository,
    IGuardianScoutLinkRepository linkRepository,
    IFundraiserEventRepository fundraiserRepository,
    IScoutAccountService accounts,
    IReimbursementService reimbursements,
    SettableClock clock)
{
    public const string CreatedBy = "Test Data";

    private static readonly (string Description, int Min, int Max)[] InvoiceKinds =
    [
        ("Monthly campout fee", 25, 45), ("Campout food", 15, 30), ("Summer camp deposit", 100, 150),
        ("Summer camp balance", 250, 375), ("Annual registration and dues", 120, 160), ("Troop T-shirt", 18, 25),
        ("Merit badge fair", 20, 40), ("Winter camp", 60, 95), ("Court of Honor dinner", 10, 20),
        ("High adventure trek deposit", 200, 350), ("Patrol gear share", 12, 30), ("Rank advancement materials", 8, 18),
    ];

    private static readonly string[] ReimbursementKinds =
    [
        "Campout groceries", "Propane and charcoal", "First aid kit refills", "Gas for troop trailer",
        "Campsite reservation", "Court of Honor supplies", "Merit badge pamphlets", "Trailer registration",
        "Dutch oven and cooking gear", "Ice and water for service project", "Awards and patches",
        "Tent repair parts", "Printing for troop handbook", "Eagle project materials",
    ];

    private static readonly string[] FundraiserKinds =
        ["Popcorn Sale", "Car Wash", "Wreath Sale", "Pancake Breakfast", "Mulch Delivery", "Camp Card Sale"];

    private static readonly string[] VoidReasons = ["Scout did not attend", "Entered twice", "Event cancelled"];

    private static readonly string[] RejectReasons =
        ["Not a troop expense", "Receipt is unreadable, please resubmit", "Duplicate of an earlier request"];

    private static readonly byte[] SampleReceipt = Encoding.ASCII.GetBytes("%PDF-1.4\n% sample receipt\n");

    public async Task<TestDataSummary> GenerateAsync(TestDataOptions options)
    {
        var scouts = await scoutRepository.GetActiveAsync();
        if (scouts.Count == 0)
        {
            throw new InvalidOperationException("There are no active scouts to create test data for.");
        }

        var guardians = (await guardianRepository.GetAllAsync()).Where(g => g.IsActive).ToList();
        if (guardians.Count == 0 && options.Reimbursements > 0)
        {
            throw new InvalidOperationException("There are no active guardians to submit test reimbursements.");
        }

        var linksByGuardian = new Dictionary<string, IReadOnlyList<GuardianScoutLinkEntity>>();
        foreach (var guardian in guardians)
        {
            linksByGuardian[guardian.GuardianId] = await linkRepository.GetScoutsForGuardianAsync(guardian.GuardianId);
        }

        var summary = new TestDataSummary();
        var random = new Random(options.Seed);
        var end = clock.Now;
        var start = end.AddMonths(-12);
        var timeline = new PriorityQueue<Func<Task>, DateTimeOffset>();

        // Anything that would happen after today simply hasn't happened yet: that is what leaves
        // recent invoices outstanding and recent receipts pending.
        void At(DateTimeOffset when, Func<Task> work)
        {
            if (when <= end)
            {
                timeline.Enqueue(work, when);
            }
        }

        DateTimeOffset RandomMoment() => start + TimeSpan.FromSeconds(random.NextDouble() * (end - start).TotalSeconds);
        TimeSpan Days(double min, double max) => TimeSpan.FromDays(min + random.NextDouble() * (max - min));
        T Pick<T>(IReadOnlyList<T> items) => items[random.Next(items.Count)];

        for (var i = 0; i < options.Fundraisers; i++)
        {
            var eventMoment = start + (end - start) * ((i + 1.0) / (options.Fundraisers + 1));
            var isLatest = i == options.Fundraisers - 1;
            var kind = FundraiserKinds[i % FundraiserKinds.Length];
            var eventId = FundraiserEventEntity.NewRowKey();
            var created = eventMoment - TimeSpan.FromDays(21);

            At(created < start ? start : created, async () =>
            {
                await fundraiserRepository.AddAsync(new FundraiserEventEntity
                {
                    PartitionKey = FundraiserEventEntity.PartitionKeyValue,
                    RowKey = eventId,
                    Name = $"{kind} ({eventMoment.ToLocalTime():MMM yyyy})",
                    EventDate = new DateTimeOffset(eventMoment.UtcDateTime.Date, TimeSpan.Zero),
                    Description = $"Troop {kind.ToLowerInvariant()}",
                    GoalAmount = random.Next(20, 41) * 100,
                    StatusValue = FundraiserEventStatus.Open,
                    CreatedUtc = clock.Now,
                    CreatedBy = CreatedBy,
                });
                summary.Fundraisers++;
            });

            // A few days after the event the money is counted and shared out by hours worked.
            At(eventMoment + TimeSpan.FromDays(3), async () =>
            {
                var fundraiser = await fundraiserRepository.GetByIdAsync(eventId) ?? throw new InvalidOperationException("Fundraiser not found.");
                fundraiser.TotalRaised = random.Next(150, 401) * 10;
                fundraiser.TotalExpenses = Math.Round(fundraiser.TotalRaised.Value * random.Next(10, 21) / 100m, 2);
                await fundraiserRepository.UpdateAsync(fundraiser);

                var workers = scouts.Where(_ => random.NextDouble() < 0.75).ToList();
                if (workers.Count == 0)
                {
                    workers.Add(Pick(scouts));
                }

                var hours = workers.Select(_ => random.Next(4, 25) / 2m).ToList();
                var ratePerHour = Math.Floor(fundraiser.NetProceeds * 0.9m / hours.Sum() * 100) / 100;
                for (var w = 0; w < workers.Count; w++)
                {
                    var share = Math.Floor(ratePerHour * hours[w] * 100) / 100;
                    if (share > 0)
                    {
                        await accounts.ApplyFundraiserAllocationAsync(workers[w].ScoutId, eventId, share, $"{kind} share", CreatedBy, hours[w]);
                        summary.Allocations++;
                    }
                }
            });

            if (!isLatest)
            {
                At(eventMoment + TimeSpan.FromDays(30), async () =>
                {
                    var fundraiser = await fundraiserRepository.GetByIdAsync(eventId) ?? throw new InvalidOperationException("Fundraiser not found.");
                    fundraiser.StatusValue = FundraiserEventStatus.Closed;
                    await fundraiserRepository.UpdateAsync(fundraiser);
                });
            }
        }

        for (var i = 0; i < options.Invoices; i++)
        {
            var issued = RandomMoment();
            At(issued, async () =>
            {
                var scout = Pick(scouts);
                var kind = Pick(InvoiceKinds);
                var total = random.Next(kind.Min, kind.Max + 1) + (random.Next(4) == 0 ? 0.50m : 0m);
                // Half the time ask to use the scout's account; the service clamps it to the balance.
                var fromAccount = random.Next(2) == 0 ? total : 0m;
                var invoice = await accounts.IssueInvoiceAsync(scout.ScoutId, total, fromAccount, issued + Days(14, 30), kind.Description, CreatedBy);
                summary.Invoices++;
                if (invoice.StatusValue != InvoiceStatus.Outstanding)
                {
                    return;
                }

                var roll = random.NextDouble();
                var isOld = end - issued > TimeSpan.FromDays(45);
                if (roll < 0.06)
                {
                    var reason = Pick(VoidReasons);
                    At(issued + Days(1, 10), async () =>
                    {
                        await accounts.VoidInvoiceAsync(scout.ScoutId, invoice.InvoiceId, reason, CreatedBy);
                        summary.InvoicesVoided++;
                    });
                }
                else if (roll < (isOld ? 0.88 : 0.40))
                {
                    var due = invoice.AmountDueFromFamily;
                    var firstPayment = random.Next(5) == 0 && due >= 20 ? Math.Round(due / 2, 2) : due;
                    var (method, reference) = PaymentDetails(random, scout.FullName);
                    var paidOn = issued + Days(2, 35);
                    At(paidOn, async () =>
                    {
                        await accounts.RecordInvoicePaymentAsync(scout.ScoutId, invoice.InvoiceId, firstPayment, method, reference, CreatedBy);
                        summary.InvoicePayments++;
                    });
                    if (firstPayment < due)
                    {
                        var (laterMethod, laterReference) = PaymentDetails(random, scout.FullName);
                        At(paidOn + Days(5, 30), async () =>
                        {
                            await accounts.RecordInvoicePaymentAsync(scout.ScoutId, invoice.InvoiceId, due - firstPayment, laterMethod, laterReference, CreatedBy);
                            summary.InvoicePayments++;
                        });
                    }
                }
            });
        }

        for (var i = 0; i < options.Reimbursements; i++)
        {
            var submitted = RandomMoment();
            At(submitted, async () =>
            {
                var guardian = Pick(guardians);
                // Most receipts are troop purchases, not tied to one scout.
                var links = linksByGuardian[guardian.GuardianId];
                var link = links.Count > 0 && random.NextDouble() < 0.3 ? Pick(links) : null;
                var amount = random.Next(600, 26001) / 100m;
                using var receipt = new MemoryStream(SampleReceipt);
                var request = await reimbursements.SubmitAsync(guardian.GuardianId, guardian.FullName, guardian.Email,
                    link?.ScoutId, link?.ScoutName, amount, Pick(ReimbursementKinds), receipt, "application/pdf", "receipt.pdf",
                    SampleReceipt.Length, submittedOnBehalfBy: random.Next(7) == 0 ? CreatedBy : null);
                summary.Reimbursements++;

                var roll = random.NextDouble();
                var isOld = end - submitted > TimeSpan.FromDays(30);
                var reviewed = submitted + Days(0.5, 6);
                if (roll < (isOld ? 0.86 : 0.50))
                {
                    At(reviewed, async () =>
                    {
                        await reimbursements.ApproveAsync(guardian.GuardianId, request.RequestId, CreatedBy, null);
                        summary.ReimbursementsApproved++;
                    });
                    if (roll < (isOld ? 0.80 : 0.25))
                    {
                        var paidHow = random.Next(3) == 0 ? "Venmo" : $"Check {random.Next(2000, 3000)}";
                        At(reviewed + Days(1, 10), async () =>
                        {
                            await reimbursements.MarkPaidAsync(guardian.GuardianId, request.RequestId, CreatedBy, paidHow);
                            summary.ReimbursementsPaid++;
                        });
                    }
                }
                else if (roll < (isOld ? 0.96 : 0.60))
                {
                    var reason = Pick(RejectReasons);
                    At(reviewed, async () =>
                    {
                        await reimbursements.RejectAsync(guardian.GuardianId, request.RequestId, CreatedBy, reason);
                        summary.ReimbursementsRejected++;
                    });
                }
            });
        }

        try
        {
            while (timeline.TryDequeue(out var work, out var when))
            {
                clock.Now = when;
                await work();
            }
        }
        finally
        {
            clock.Now = end;
        }

        return summary;
    }

    private static (string Method, string Reference) PaymentDetails(Random random, string scoutName) => random.Next(20) switch
    {
        < 10 => (InvoicePaymentMethod.Check, $"Check {random.Next(1000, 10000)}"),
        < 15 => (InvoicePaymentMethod.Venmo, $"@{scoutName.Split(' ')[^1].ToLowerInvariant()}-family"),
        < 19 => (InvoicePaymentMethod.Cash, "Paid at troop meeting"),
        _ => (InvoicePaymentMethod.Other, "Zelle"),
    };
}
