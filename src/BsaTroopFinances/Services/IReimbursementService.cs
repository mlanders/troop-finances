using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

public interface IReimbursementService
{
    Task<ReimbursementRequestEntity> SubmitAsync(
        string guardianId, string submitterName, string submitterEmail, string? scoutId, string? scoutName,
        decimal amountRequested, string description, Stream receiptContent, string receiptContentType,
        string receiptFileName, long receiptSizeBytes, string? submittedOnBehalfBy = null);

    Task ApproveAsync(string guardianId, string requestId, string reviewedBy, string? notes);

    Task RejectAsync(string guardianId, string requestId, string reviewedBy, string notes);

    Task ReopenAsync(string guardianId, string requestId, string reopenedBy, string? notes);

    Task MarkPaidAsync(string guardianId, string requestId, string paidBy, string? paymentMethodNote);

    Task UpdateAmountAsync(string guardianId, string requestId, decimal amount, string updatedBy);

    Task DeletePendingAsync(string guardianId, string requestId);
}
