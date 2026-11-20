using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IInvoiceRepository
{
    /// <summary>A scout's invoices -- one partition query.</summary>
    Task<IReadOnlyList<InvoiceEntity>> GetByScoutAsync(string scoutId);

    /// <summary>Every invoice (optionally only those issued in [issuedFrom, issuedTo]), newest
    /// first. For reports; list pages use GetPageAsync instead.</summary>
    Task<IReadOnlyList<InvoiceEntity>> GetAllAsync(DateTimeOffset? issuedFrom = null, DateTimeOffset? issuedTo = null);

    /// <summary>Invoices billed to this guardian as an adult (not their scouts' invoices).</summary>
    Task<IReadOnlyList<InvoiceEntity>> GetByGuardianAsync(string guardianId);

    /// <summary>The ids of every guardian who has ever been billed directly.</summary>
    Task<IReadOnlySet<string>> GetGuardianIdsWithInvoicesAsync();

    /// <summary>Key lookup. ownerId is the scout's id, or the guardian's for an invoice billed to an adult.</summary>
    Task<InvoiceEntity?> GetByIdAsync(string ownerId, string invoiceId);

    /// <summary>Lookup by invoice id alone (indexed), for links that don't carry the scout id.</summary>
    Task<InvoiceEntity?> GetByInvoiceIdAsync(string invoiceId);

    Task AddAsync(InvoiceEntity invoice);

    Task UpdateAsync(InvoiceEntity invoice);

    /// <summary>One page of invoices, newest first, filtered in the database by status (null = all),
    /// issue date and, when <paramref name="nameSearch"/> is given, by text found in the scout's
    /// name, in the name of a guardian linked to the scout, or in the name of the adult the invoice
    /// is billed to; with the total number of matches.</summary>
    Task<PagedResult<InvoiceEntity>> GetPageAsync(string? status, DateTimeOffset? issuedFrom, DateTimeOffset? issuedTo, int page, int pageSize, string? nameSearch = null, string? descriptionSearch = null);

    /// <summary>Outstanding invoices for any of these scouts (e.g. one family's).</summary>
    Task<IReadOnlyList<InvoiceEntity>> GetOutstandingForScoutsAsync(IReadOnlyCollection<string> scoutIds);

    /// <summary>How many invoices are outstanding and how much is still due on them.</summary>
    Task<(int Count, decimal Total)> GetOutstandingSummaryAsync();
}
