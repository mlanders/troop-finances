using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Models;

/// <summary>A scout's full financial picture for ScoutDetail.razor / the portal's ScoutBalance.razor:
/// current balance plus every ledger entry and invoice, for display as one combined statement.</summary>
public record ScoutStatementModel(
    ScoutEntity Scout,
    IReadOnlyList<ScoutLedgerEntryEntity> LedgerEntries,
    IReadOnlyList<InvoiceEntity> Invoices);
