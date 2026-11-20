using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Tests;

/// <summary>The real repositories against a real SQL database (LocalDB).</summary>
public class SqlRepositoryTests(SqlTestDatabase sql) : IClassFixture<SqlTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 18, 0, 0, TimeSpan.Zero);

    private async Task ReadyAsync()
    {
        Skip.IfNot(sql.Available, sql.UnavailableReason);
        await sql.ResetAsync();
    }

    private static InvoiceEntity Invoice(int n, string status, string scoutId = "scout1", decimal due = 10m)
    {
        var id = $"inv{n:D3}";
        return new InvoiceEntity
        {
            PartitionKey = scoutId,
            RowKey = id,
            InvoiceId = id,
            ScoutId = scoutId,
            ScoutName = "Alex Rivera",
            Description = $"Invoice {n}",
            TotalAmount = due,
            AmountDueFromFamily = status == InvoiceStatus.Outstanding ? due : 0,
            StatusValue = status,
            IssuedUtc = Start.AddDays(n),
        };
    }

    [SkippableFact]
    public async Task Invoice_pages_are_filtered_counted_and_newest_first_in_the_database()
    {
        await ReadyAsync();
        var invoices = new InvoiceRepository(sql.Db);
        // 60 invoices: every third is paid, the rest outstanding.
        for (var n = 1; n <= 60; n++)
        {
            await invoices.AddAsync(Invoice(n, n % 3 == 0 ? InvoiceStatus.Paid : InvoiceStatus.Outstanding));
        }

        var page1 = await invoices.GetPageAsync(InvoiceStatus.Outstanding, null, null, 1, 25);
        var page2 = await invoices.GetPageAsync(InvoiceStatus.Outstanding, null, null, 2, 25);

        Assert.Equal(40, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages(25));
        Assert.Equal(25, page1.Items.Count);
        Assert.Equal(15, page2.Items.Count);
        Assert.All(page1.Items.Concat(page2.Items), i => Assert.Equal(InvoiceStatus.Outstanding, i.StatusValue));
        Assert.Equal("inv059", page1.Items[0].InvoiceId);                       // newest first
        Assert.Empty(page1.Items.Select(i => i.InvoiceId).Intersect(page2.Items.Select(i => i.InvoiceId)));

        var all = await invoices.GetPageAsync(null, null, null, 1, 25);
        Assert.Equal(60, all.TotalCount);

        var range = await invoices.GetPageAsync(null, Start.AddDays(10), Start.AddDays(19), 1, 25);
        Assert.Equal(10, range.TotalCount);
        Assert.Equal("inv019", range.Items[0].InvoiceId);
    }

    [SkippableFact]
    public async Task Invoice_pages_can_be_narrowed_by_scout_or_guardian_name()
    {
        await ReadyAsync();
        var invoices = new InvoiceRepository(sql.Db);
        await invoices.AddAsync(Invoice(1, InvoiceStatus.Outstanding, scoutId: "alex"));            // scout "Alex Rivera"
        var jordan = Invoice(2, InvoiceStatus.Outstanding, scoutId: "jordan");
        jordan.ScoutName = "Jordan Lee";
        await invoices.AddAsync(jordan);
        var paid = Invoice(3, InvoiceStatus.Paid, scoutId: "jordan");
        paid.ScoutName = "Jordan Lee";
        await invoices.AddAsync(paid);
        // The guardian linked to Jordan has a different surname.
        await new GuardianRepository(sql.Db).AddAsync(new GuardianEntity
        {
            PartitionKey = GuardianEntity.PartitionKeyValue, RowKey = "pat@example.com", GuardianId = "pat", FullName = "Pat Okafor", Email = "pat@example.com",
        });
        await new GuardianScoutLinkRepository(sql.Db).AddLinkAsync(new GuardianScoutLinkEntity
        {
            PartitionKey = "pat", RowKey = "jordan", GuardianId = "pat", ScoutId = "jordan", ScoutName = "Jordan Lee",
        });

        async Task<string[]> Found(string? status, string search) =>
            (await invoices.GetPageAsync(status, null, null, 1, 25, search)).Items.Select(i => i.InvoiceId).Order().ToArray();

        Assert.Equal(["inv001"], await Found(null, "rivera"));                 // scout name, any case
        Assert.Equal(["inv002", "inv003"], await Found(null, "Okaf"));         // guardian name finds their scout's invoices
        Assert.Equal(["inv002"], await Found(InvoiceStatus.Outstanding, "okafor"));   // combined with the status filter
        Assert.Empty(await Found(null, "nobody"));
        Assert.Equal(2, (await invoices.GetPageAsync(null, null, null, 1, 25, "Jordan")).TotalCount);
        Assert.Empty(await Found(null, "%"));                                  // typed text is not a wildcard

        // An invoice billed to an adult is found by that adult's name, and listed under them.
        await invoices.AddAsync(new InvoiceEntity
        {
            PartitionKey = "pat", RowKey = "inv900", InvoiceId = "inv900", GuardianId = "pat", GuardianName = "Pat Okafor", Description = "Summer camp, adult",
            TotalAmount = 320m, AmountDueFromFamily = 320m, StatusValue = InvoiceStatus.Outstanding, IssuedUtc = Start,
        });
        Assert.Equal(["inv002", "inv003", "inv900"], await Found(null, "Okaf"));
        Assert.Equal("inv900", Assert.Single(await invoices.GetByGuardianAsync("pat")).InvoiceId);
        Assert.Equal(["pat"], await invoices.GetGuardianIdsWithInvoicesAsync());
        var adult = await invoices.GetByIdAsync("pat", "inv900");
        Assert.True(adult!.IsForGuardian);
        Assert.Equal("Pat Okafor", adult.BilledTo);
        Assert.Empty(await invoices.GetByScoutAsync(""));                       // not mistaken for a scout's
    }

    [SkippableFact]
    public async Task Reimbursement_pages_can_be_narrowed_by_scout_or_guardian_name()
    {
        await ReadyAsync();
        var requests = new ReimbursementRequestRepository(sql.Db);
        async Task Add(string id, string guardianId, string submitter, string? scout, string status = ReimbursementStatus.Pending) =>
            await requests.AddAsync(new ReimbursementRequestEntity
            {
                PartitionKey = guardianId, RowKey = id, RequestId = id, SubmitterGuardianId = guardianId, SubmitterName = submitter,
                ScoutName = scout, StatusValue = status, SubmittedUtc = Start,
            });
        await Add("r1", "maria", "Maria Rivera", "Alex Rivera");
        await Add("r2", "maria", "Maria Rivera", null);                         // general troop purchase
        await Add("r3", "pat", "Pat Okafor", "Jordan Lee", ReimbursementStatus.Paid);
        await Add("r4", "chris", "Chris Novak", null);
        // Pat is linked to Jordan, so a troop purchase of Pat's turns up when searching for Jordan.
        await new GuardianScoutLinkRepository(sql.Db).AddLinkAsync(new GuardianScoutLinkEntity
        {
            PartitionKey = "pat", RowKey = "jordan", GuardianId = "pat", ScoutId = "jordan", ScoutName = "Jordan Lee",
        });
        await Add("r5", "pat", "Pat Okafor", null);

        async Task<string[]> Found(string? status, string search) =>
            (await requests.GetPageAsync(status, null, null, 1, 25, search)).Items.Select(r => r.RequestId).Order().ToArray();

        Assert.Equal(["r1", "r2"], await Found(null, "rivera"));                // guardian or scout name, any case
        Assert.Equal(["r3", "r5"], await Found(null, "jordan"));                // the scout's own receipt and their guardian's
        Assert.Equal(["r5"], await Found(ReimbursementStatus.Pending, "Okaf")); // combined with the status filter
        Assert.Equal(["r4"], await Found(null, "novak"));
        Assert.Empty(await Found(null, "nobody"));
        Assert.Empty(await Found(null, "%"));                                   // typed text is not a wildcard
        Assert.Equal(5, (await requests.GetPageAsync(null, null, null, 1, 25)).TotalCount);
    }

    [SkippableFact]
    public async Task Description_searches_combine_with_the_date_range_on_both_lists()
    {
        await ReadyAsync();
        var invoices = new InvoiceRepository(sql.Db);
        var camp = Invoice(1, InvoiceStatus.Outstanding);
        camp.Description = "Summer Camp 2026 fees";
        await invoices.AddAsync(camp);
        var lateCamp = Invoice(200, InvoiceStatus.Outstanding);
        lateCamp.Description = "Summer camp 2026 balance";
        await invoices.AddAsync(lateCamp);
        await invoices.AddAsync(Invoice(2, InvoiceStatus.Outstanding));

        var inRange = await invoices.GetPageAsync(null, Start, Start.AddDays(30), 1, 25, descriptionSearch: "summer camp");
        Assert.Equal([camp.InvoiceId], inRange.Items.Select(i => i.InvoiceId));
        Assert.Equal(2, (await invoices.GetPageAsync(null, null, null, 1, 25, descriptionSearch: " SUMMER CAMP ")).TotalCount);

        var requests = new ReimbursementRequestRepository(sql.Db);
        await requests.AddAsync(new ReimbursementRequestEntity
        {
            PartitionKey = "maria", RowKey = "r1", RequestId = "r1", Description = "Propane for the campout", StatusValue = ReimbursementStatus.Pending, SubmittedUtc = Start,
        });
        await requests.AddAsync(new ReimbursementRequestEntity
        {
            PartitionKey = "maria", RowKey = "r2", RequestId = "r2", Description = "Ice", StatusValue = ReimbursementStatus.Pending, SubmittedUtc = Start,
        });
        Assert.Equal(["r1"], (await requests.GetPageAsync(ReimbursementStatus.Pending, null, null, 1, 25, descriptionSearch: "propane")).Items.Select(r => r.RequestId));
    }

    [SkippableFact]
    public async Task Saved_views_are_kept_per_list_and_can_be_deleted()
    {
        await ReadyAsync();
        var views = new ListViewRepository(sql.Db);
        await views.AddAsync(new ListViewEntity
        {
            PartitionKey = ListViewEntity.Invoices, RowKey = "v2", Name = "Summer camp 2026", Status = InvoiceStatus.Paid,
            FromDate = new DateOnly(2026, 6, 1), ToDate = new DateOnly(2026, 8, 31), DescriptionSearch = "summer camp", CreatedBy = "pat@example.com", CreatedUtc = Start,
        });
        await views.AddAsync(new ListViewEntity { PartitionKey = ListViewEntity.Invoices, RowKey = "v1", Name = "Adults", CreatedBy = "pat@example.com", CreatedUtc = Start });
        await views.AddAsync(new ListViewEntity { PartitionKey = ListViewEntity.Reimbursements, RowKey = "v3", Name = "Propane", CreatedBy = "pat@example.com", CreatedUtc = Start });

        Assert.Equal(["Adults", "Summer camp 2026"], (await views.GetForListAsync(ListViewEntity.Invoices)).Select(v => v.Name));
        var camp = await views.GetAsync(ListViewEntity.Invoices, "v2");
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31), "summer camp"), (camp!.FromDate, camp.ToDate, camp.DescriptionSearch));
        Assert.Null(await views.GetAsync(ListViewEntity.Reimbursements, "v2"));

        await views.DeleteAsync(ListViewEntity.Invoices, "v2");
        Assert.Equal(["Adults"], (await views.GetForListAsync(ListViewEntity.Invoices)).Select(v => v.Name));
        Assert.Single(await views.GetForListAsync(ListViewEntity.Reimbursements));
    }

    [SkippableFact]
    public async Task Dashboard_summaries_are_counted_and_summed_by_the_database()
    {
        await ReadyAsync();
        var invoices = new InvoiceRepository(sql.Db);
        await invoices.AddAsync(Invoice(1, InvoiceStatus.Outstanding, due: 175m));
        await invoices.AddAsync(Invoice(2, InvoiceStatus.Outstanding, due: 45.50m));
        await invoices.AddAsync(Invoice(3, InvoiceStatus.Paid, due: 60m));

        var requests = new ReimbursementRequestRepository(sql.Db);
        foreach (var (id, status, amount) in new[] { ("r1", ReimbursementStatus.Pending, 42.18m), ("r2", ReimbursementStatus.Pending, 19.99m), ("r3", ReimbursementStatus.Paid, 64.50m) })
        {
            await requests.AddAsync(new ReimbursementRequestEntity
            {
                PartitionKey = "maria", RowKey = id, RequestId = id, StatusValue = status, AmountRequested = amount, SubmittedUtc = Start,
            });
        }

        Assert.Equal((2, 220.50m), await invoices.GetOutstandingSummaryAsync());
        Assert.Equal((2, 62.17m), await requests.GetStatusSummaryAsync(ReimbursementStatus.Pending));
        Assert.Equal(42.18m, (await requests.GetByRequestIdAsync("r1"))!.AmountRequested);   // exact decimal round trip
    }

    [SkippableFact]
    public async Task A_guardians_receipts_are_paged_filtered_and_counted_by_status_in_the_database()
    {
        await ReadyAsync();
        var requests = new ReimbursementRequestRepository(sql.Db);
        // Maria: 14 paid and 3 pending, submitted on successive days. Chris: one pending, never Maria's.
        for (var n = 1; n <= 17; n++)
        {
            await requests.AddAsync(new ReimbursementRequestEntity
            {
                PartitionKey = "maria", RowKey = $"r{n:D2}", RequestId = $"r{n:D2}", SubmittedUtc = Start.AddDays(n),
                StatusValue = n <= 14 ? ReimbursementStatus.Paid : ReimbursementStatus.Pending,
            });
        }
        await requests.AddAsync(new ReimbursementRequestEntity { PartitionKey = "chris", RowKey = "c1", RequestId = "c1", SubmittedUtc = Start, StatusValue = ReimbursementStatus.Pending });

        var counts = await requests.GetSubmitterStatusCountsAsync("maria");
        var paidPage2 = await requests.GetSubmitterPageAsync("maria", ReimbursementStatus.Paid, 2, 10);
        var allPage1 = await requests.GetSubmitterPageAsync("maria", null, 1, 10);

        Assert.Equal(2, counts.Count);
        Assert.Equal(14, counts[ReimbursementStatus.Paid]);
        Assert.Equal(3, counts[ReimbursementStatus.Pending]);
        Assert.Equal(14, paidPage2.TotalCount);
        Assert.Equal(["r04", "r03", "r02", "r01"], paidPage2.Items.Select(r => r.RequestId));
        Assert.Equal(17, allPage1.TotalCount);
        Assert.Equal("r17", allPage1.Items[0].RequestId);            // newest first
        Assert.Equal(10, allPage1.Items.Count);
    }

    [SkippableFact]
    public async Task Ledger_entries_move_the_cached_balance_and_total_by_source()
    {
        await ReadyAsync();
        var scouts = new ScoutRepository(sql.Db, TimeProvider.System);
        await scouts.AddAsync(new ScoutEntity { PartitionKey = ScoutEntity.PartitionKeyValue, RowKey = "scout1", FullName = "Alex Rivera" });
        var ledger = new ScoutLedgerRepository(sql.Db, scouts);

        async Task Append(string type, string source, string sourceId, decimal amount) => await ledger.AppendEntryAsync(new ScoutLedgerEntryEntity
        {
            PartitionKey = "scout1", RowKey = ScoutLedgerEntryEntity.NewRowKey(), EntryTypeValue = type,
            SourceTypeValue = source, SourceId = sourceId, Amount = amount, CreatedUtc = Start,
        });

        await Append(LedgerEntryType.Credit, LedgerSourceType.FundraiserAllocation, "popcorn", 150.10m);
        await Append(LedgerEntryType.Credit, LedgerSourceType.FundraiserAllocation, "popcorn", 49.90m);
        await Append(LedgerEntryType.Credit, LedgerSourceType.FundraiserAllocation, "carwash", 20m);
        await Append(LedgerEntryType.Debit, LedgerSourceType.InvoiceApplied, "inv001", 70.25m);

        Assert.Equal(149.75m, (await scouts.GetByIdAsync("scout1"))!.CachedBalance);
        var totals = await ledger.GetTotalsBySourceAsync(LedgerSourceType.FundraiserAllocation, ["popcorn", "carwash", "none"]);
        Assert.Equal(200m, totals["popcorn"]);
        Assert.Equal(20m, totals["carwash"]);
        Assert.False(totals.ContainsKey("none"));
        Assert.Equal(2, (await ledger.GetForSourceAsync(LedgerSourceType.FundraiserAllocation, "popcorn")).Count);
    }

    [SkippableFact]
    public async Task A_stale_copy_cannot_overwrite_newer_changes()
    {
        await ReadyAsync();
        var invoices = new InvoiceRepository(sql.Db);
        await invoices.AddAsync(Invoice(1, InvoiceStatus.Outstanding));

        var first = (await invoices.GetByInvoiceIdAsync("inv001"))!;
        var second = (await invoices.GetByInvoiceIdAsync("inv001"))!;
        first.Description = "Edited first";
        await invoices.UpdateAsync(first);

        second.Description = "Edited from a stale copy";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => invoices.UpdateAsync(second));
        Assert.Equal("Edited first", (await invoices.GetByInvoiceIdAsync("inv001"))!.Description);
    }

    [SkippableFact]
    public async Task Two_guardians_cannot_share_an_email()
    {
        await ReadyAsync();
        var guardians = new GuardianRepository(sql.Db);
        GuardianEntity Guardian(string id) => new()
        {
            PartitionKey = GuardianEntity.PartitionKeyValue, RowKey = GuardianEntity.NormalizeEmail("Maria@Example.com"),
            GuardianId = id, FullName = "Maria Rivera", Email = "maria@example.com",
        };

        await guardians.AddAsync(Guardian("g1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => guardians.AddAsync(Guardian("g2")));
        Assert.Equal("g1", (await guardians.GetByEmailAsync("MARIA@example.com"))!.GuardianId);
    }

    [SkippableFact]
    public async Task Upserts_replace_instead_of_duplicating()
    {
        await ReadyAsync();
        var codes = new LoginCodeRepository(sql.Db);
        LoginCodeEntity Code(string hash) => new()
        {
            PartitionKey = LoginCodeEntity.PartitionKeyValue, RowKey = "maria@example.com", CodeHash = hash, ExpiresUtc = Start,
        };
        await codes.UpsertAsync(Code("first"));
        await codes.UpsertAsync(Code("second"));
        Assert.Equal("second", (await codes.GetByEmailAsync("maria@example.com"))!.CodeHash);

        var settings = new EmailSettingsRepository(sql.Db);
        await settings.SaveAsync(new EmailSettingsEntity { InvoiceEnabled = false });
        await settings.SaveAsync(new EmailSettingsEntity { InvoiceEnabled = true, AdminRecipients = "a@example.com" });
        var saved = await settings.GetAsync();
        Assert.True(saved.InvoiceEnabled);
        Assert.Equal(["a@example.com"], saved.GetAdminRecipients());

        var links = new GuardianScoutLinkRepository(sql.Db);
        var link = new GuardianScoutLinkEntity { PartitionKey = "g1", RowKey = "scout1", GuardianId = "g1", ScoutId = "scout1", ScoutName = "Old" };
        await links.AddLinkAsync(link);
        await links.AddLinkAsync(new GuardianScoutLinkEntity { PartitionKey = "g1", RowKey = "scout1", GuardianId = "g1", ScoutId = "scout1", ScoutName = "New" });
        Assert.Equal("New", Assert.Single(await links.GetGuardiansForScoutAsync("scout1")).ScoutName);
    }
}
