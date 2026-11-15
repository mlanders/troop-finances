using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class InvoiceRepository(FinanceDb db) : IInvoiceRepository
{
    public async Task<IReadOnlyList<InvoiceEntity>> GetByScoutAsync(string scoutId) =>
        await db.ReadAsync(async c => await c.Invoices.AsNoTracking()
            .Where(i => i.ScoutId == scoutId && i.GuardianId == null).OrderByDescending(i => i.RowKey).ToListAsync());

    public async Task<IReadOnlyList<InvoiceEntity>> GetByGuardianAsync(string guardianId) =>
        await db.ReadAsync(async c => await c.Invoices.AsNoTracking()
            .Where(i => i.GuardianId == guardianId).OrderByDescending(i => i.RowKey).ToListAsync());

    public async Task<IReadOnlySet<string>> GetGuardianIdsWithInvoicesAsync() =>
        await db.ReadAsync(async c => (await c.Invoices
            .Where(i => i.GuardianId != null).Select(i => i.GuardianId!).Distinct().ToListAsync()).ToHashSet());

    public async Task<IReadOnlyList<InvoiceEntity>> GetAllAsync(DateTimeOffset? issuedFrom = null, DateTimeOffset? issuedTo = null) =>
        await db.ReadAsync(async c => await Filter(c.Invoices.AsNoTracking(), null, issuedFrom, issuedTo)
            .OrderByDescending(i => i.IssuedUtc).ToListAsync());

    public async Task<PagedResult<InvoiceEntity>> GetPageAsync(string? status, DateTimeOffset? issuedFrom, DateTimeOffset? issuedTo, int page, int pageSize, string? nameSearch = null, string? descriptionSearch = null) =>
        await db.ReadAsync(async c =>
        {
            var query = Filter(c.Invoices.AsNoTracking(), status, issuedFrom, issuedTo);
            if (!string.IsNullOrWhiteSpace(nameSearch))
            {
                // Lower-cased on both sides: SQL Server ignores case anyway, PostgreSQL doesn't.
                var text = nameSearch.Trim().ToLowerInvariant();
                query = query.Where(i => i.ScoutName.ToLower().Contains(text)
                    || (i.GuardianName != null && i.GuardianName.ToLower().Contains(text))
                    || c.GuardianScoutLinks.Any(l => l.ScoutId == i.ScoutId
                        && c.Guardians.Any(g => g.GuardianId == l.GuardianId && g.FullName.ToLower().Contains(text))));
            }
            if (!string.IsNullOrWhiteSpace(descriptionSearch))
            {
                var description = descriptionSearch.Trim().ToLowerInvariant();
                query = query.Where(i => i.Description.ToLower().Contains(description));
            }
            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(i => i.IssuedUtc).ThenByDescending(i => i.RowKey)
                .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
                .ToListAsync();
            return new PagedResult<InvoiceEntity>(items, total);
        });

    public async Task<IReadOnlyList<InvoiceEntity>> GetOutstandingForScoutsAsync(IReadOnlyCollection<string> scoutIds) =>
        await db.ReadAsync(async c => await c.Invoices.AsNoTracking()
            .Where(i => i.StatusValue == InvoiceStatus.Outstanding && scoutIds.Contains(i.ScoutId))
            .ToListAsync());

    public async Task<(int Count, decimal Total)> GetOutstandingSummaryAsync() =>
        await db.ReadAsync(async c =>
        {
            var outstanding = c.Invoices.Where(i => i.StatusValue == InvoiceStatus.Outstanding);
            return (await outstanding.CountAsync(), await outstanding.SumAsync(i => (decimal?)i.AmountDueFromFamily) ?? 0);
        });

    public async Task<InvoiceEntity?> GetByIdAsync(string ownerId, string invoiceId) =>
        await db.ReadAsync(c => c.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.PartitionKey == InvoiceEntity.BuildPartitionKey(ownerId) && i.RowKey == invoiceId));

    public async Task<InvoiceEntity?> GetByInvoiceIdAsync(string invoiceId) =>
        await db.ReadAsync(c => c.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.InvoiceId == invoiceId));

    public Task AddAsync(InvoiceEntity invoice) => db.WriteAsync(c =>
    {
        if (string.IsNullOrEmpty(invoice.InvoiceId))
        {
            invoice.InvoiceId = invoice.RowKey;
        }
        c.Invoices.Add(invoice);
        return Task.CompletedTask;
    });

    public Task UpdateAsync(InvoiceEntity invoice) => db.WriteAsync(c => c.UpdateDetachedAsync(invoice));

    private static IQueryable<InvoiceEntity> Filter(IQueryable<InvoiceEntity> query, string? status, DateTimeOffset? issuedFrom, DateTimeOffset? issuedTo)
    {
        if (status is not null)
        {
            query = query.Where(i => i.StatusValue == status);
        }
        if (issuedFrom is { } from)
        {
            query = query.Where(i => i.IssuedUtc >= from);
        }
        if (issuedTo is { } to)
        {
            query = query.Where(i => i.IssuedUtc <= to);
        }
        return query;
    }
}
