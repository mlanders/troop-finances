using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class EmailLogRepository(FinanceDb db) : IEmailLogRepository
{
    public Task AddAsync(EmailLogEntity entry) => db.WriteAsync(c =>
    {
        c.EmailLog.Add(entry);
        return Task.CompletedTask;
    });

    public async Task<IReadOnlyList<EmailLogEntity>> GetSinceAsync(DateTimeOffset since) =>
        await db.ReadAsync(async c => await c.EmailLog.AsNoTracking()
            .Where(e => e.SentUtc >= since).OrderByDescending(e => e.SentUtc).ToListAsync());
}
