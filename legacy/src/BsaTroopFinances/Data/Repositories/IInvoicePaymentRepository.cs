using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IInvoicePaymentRepository
{
    Task<IReadOnlyList<InvoicePaymentEntity>> GetByInvoiceAsync(string invoiceId);
    Task AddAsync(InvoicePaymentEntity payment);

    /// <summary>Every payment (across all invoices) received in [paidFrom, paidTo), for monthly
    /// reporting.</summary>
    Task<IReadOnlyList<InvoicePaymentEntity>> GetPaidBetweenAsync(DateTimeOffset paidFrom, DateTimeOffset paidTo);
}
