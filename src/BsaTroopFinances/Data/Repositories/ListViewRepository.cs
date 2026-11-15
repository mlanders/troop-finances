using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class ListViewRepository(FinanceDb db) : IListViewRepository
{
    public async Task<IReadOnlyList<ListViewEntity>> GetForListAsync(string list) =>
        await db.ReadAsync(async c => await c.ListViews.AsNoTracking()
            .Where(v => v.PartitionKey == list)
            .OrderBy(v => v.Name).ThenBy(v => v.RowKey)
            .ToListAsync());

    public Task<ListViewEntity?> GetAsync(string list, string viewId) =>
        db.ReadAsync(c => c.ListViews.AsNoTracking()
            .FirstOrDefaultAsync(v => v.PartitionKey == list && v.RowKey == viewId));

    public Task AddAsync(ListViewEntity view) => db.WriteAsync(c =>
    {
        c.ListViews.Add(view);
        return Task.CompletedTask;
    });

    public Task DeleteAsync(string list, string viewId) => db.WriteAsync(c =>
        c.DeleteByKeyAsync<ListViewEntity>(list, viewId));
}
