using BsaTroopFinances.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BsaTroopFinances.Data.Repositories;

public class InvoicePaymentRepository(FinanceDb db) : IInvoicePaymentRepository
{
    public async Task<IReadOnlyList<InvoicePaymentEntity>> GetByInvoiceAsync(string invoiceId) =>
        await db.ReadAsync(async c => await c.InvoicePayments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId).OrderByDescending(p => p.RowKey).ToListAsync());

    public Task AddAsync(InvoicePaymentEntity payment) => db.WriteAsync(c =>
    {
        c.InvoicePayments.Add(payment);
        return Task.CompletedTask;
    });

    public async Task<IReadOnlyList<InvoicePaymentEntity>> GetPaidBetweenAsync(DateTimeOffset paidFrom, DateTimeOffset paidTo) =>
        await db.ReadAsync(async c => await c.InvoicePayments.AsNoTracking()
            .Where(p => p.PaidUtc >= paidFrom && p.PaidUtc < paidTo).ToListAsync());
}
