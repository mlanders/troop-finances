using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Models;

namespace BsaTroopFinances.Services;

public interface IScoutAccountService
{
    /// <summary>Credits a scout's account from a fundraiser's proceeds.</summary>
    Task ApplyFundraiserAllocationAsync(string scoutId, string eventId, decimal amount, string description, string createdBy, decimal? hoursWorked = null);

    /// <summary>
    /// Issues an invoice for a charge (trip/camp/meal/etc). amountToApplyFromAccount is clamped
    /// to the scout's balance at issue time -- this is a normal flow and can never push the
    /// balance negative. The remainder (if any) is AmountDueFromFamily and the invoice starts
    /// Outstanding; if the whole amount was covered by the account it starts Paid.
    /// </summary>
    Task<InvoiceEntity> IssueInvoiceAsync(
        string scoutId, decimal totalAmount, decimal amountToApplyFromAccount, DateTimeOffset dueDate,
        string description, string issuedBy);

    /// <summary>
    /// Issues an invoice billed to an adult rather than a scout (e.g. a parent's own place at
    /// summer camp). The whole amount is due from the guardian: scout funds are never applied to
    /// it, at issue or later.
    /// </summary>
    Task<InvoiceEntity> IssueGuardianInvoiceAsync(string guardianId, decimal totalAmount, DateTimeOffset dueDate, string description, string issuedBy);

    // In the calls below, ownerId is the invoice's OwnerId: the scout's id, or the guardian's for
    // an invoice billed to an adult.

    Task MarkInvoicePaidAsync(string ownerId, string invoiceId, string paidBy);

    Task RecordInvoicePaymentAsync(string ownerId, string invoiceId, decimal amount, string method, string? reference, string paidBy);

    /// <summary>
    /// One check covering several invoices (typically one family's, across siblings), optionally
    /// with part of each scout invoice covered from that scout's own account. Every line is checked
    /// first (outstanding, both parts together within what's due, no duplicates, scout funds only on
    /// a scout's invoice and never more than that scout's balance across their lines) and nothing is
    /// written if any fails. The scout-account part of a line is recorded first, as a Scout funds
    /// payment that debits the scout; the family's part (check, cash, Venmo or other) follows, each
    /// sharing the returned PaymentGroupId and the family's total -- so a bank deposit sees just the
    /// check. When scout funds cover everything, there's no family part and the method is unused.
    /// </summary>
    Task<string> RecordFamilyPaymentAsync(IReadOnlyList<FamilyPaymentLine> lines, string method, string? reference, string paidBy);

    /// <summary>Voids an invoice. If it had applied money from the scout's account, appends a
    /// reversing credit ledger entry restoring that amount -- the original debit is never
    /// mutated or deleted, preserving history.</summary>
    Task VoidInvoiceAsync(string ownerId, string invoiceId, string reason, string voidedBy);

    /// <summary>
    /// The one path that can push a scout's balance negative -- a manual correction with a
    /// required reason, bypassing the zero-floor clamp used by the normal flows above.
    /// </summary>
    Task ApplyManualAdjustmentAsync(string scoutId, decimal amount, string reason, string createdBy);

    /// <summary>Renames a scout, records the change as a zero-amount entry in the scout's ledger,
    /// and updates the copies of the name kept on guardian links, invoices and reimbursement
    /// requests so the new name shows everywhere.</summary>
    Task RenameScoutAsync(string scoutId, string newFullName, string changedBy);

    /// <summary>
    /// Moves money off a scout's account, up to their balance, with a required reason: to another
    /// scout (debit here, credit there) or to the troop's general fund (debit only -- the money is
    /// already in the troop's bank and stops counting as held for scouts). One transaction.
    /// </summary>
    Task TransferBalanceAsync(string scoutId, BalanceDestination to, decimal amount, string reason, string transferredBy);

    /// <summary>
    /// Marks a scout inactive. With <paramref name="moveBalanceTo"/>, their whole positive balance
    /// moves there first (a reason is required); without it, any balance stays on the account.
    /// Both happen in one transaction and are recorded in Activity History.
    /// </summary>
    Task DeactivateAsync(string scoutId, BalanceDestination? moveBalanceTo, string? reason, string deactivatedBy);

    Task ReactivateAsync(string scoutId, string reactivatedBy);

    Task<ScoutStatementModel?> GetScoutStatementAsync(string scoutId);

    /// <summary>Permanently deletes a scout who has never had any invoices or ledger activity
    /// (throws otherwise) -- also removes any guardian links, since those are just visibility
    /// relationships rather than financial history. Logs a FinanceAudit entry before deleting,
    /// since this is the one irreversible action available on a scout.</summary>
    Task DeleteScoutAsync(string scoutId, string deletedBy);
}

/// <summary>Where money moved off a scout's account goes: another scout, or (ScoutId null) the
/// troop's general fund.</summary>
public sealed record BalanceDestination(string? ScoutId)
{
    public static BalanceDestination GeneralFund { get; } = new((string?)null);

    public static BalanceDestination ToScout(string scoutId) => new(scoutId);

    public bool IsGeneralFund => ScoutId is null;
}

/// <summary>One invoice's share of a family payment. OwnerId is the invoice's OwnerId. Amount is
/// what the family pays (check, cash, ...); FromScoutAccount is what the invoice's own scout's
/// account covers, if anything.</summary>
public record FamilyPaymentLine(string OwnerId, string InvoiceId, decimal Amount, decimal FromScoutAccount = 0);
