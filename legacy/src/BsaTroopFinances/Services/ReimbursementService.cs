using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

public class ReimbursementService(
    IReimbursementRequestRepository requestRepository,
    IReceiptStorageService receiptStorage,
    IFinanceAuditRepository auditRepository,
    INotificationService notificationService,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IReimbursementService
{
    // Each change saves the request and its audit entry in one transaction; emails go out only
    // after it commits.

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "application/pdf",
    };

    private const long MaxReceiptSizeBytes = 10 * 1024 * 1024; // 10 MB

    public async Task<ReimbursementRequestEntity> SubmitAsync(
        string guardianId, string submitterName, string submitterEmail, string? scoutId, string? scoutName,
        decimal amountRequested, string description, Stream receiptContent, string receiptContentType,
        string receiptFileName, long receiptSizeBytes, string? submittedOnBehalfBy = null)
    {
        if (amountRequested <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountRequested), "Amount must be positive.");
        }

        // Client-side (InputFile.maxAllowedSize / accept filter) checks are trivially bypassable,
        // so re-validate here regardless of what the browser already enforced.
        if (!AllowedContentTypes.Contains(receiptContentType))
        {
            throw new ArgumentException("Receipts must be a JPEG, PNG, or PDF file.", nameof(receiptContentType));
        }

        if (receiptSizeBytes > MaxReceiptSizeBytes)
        {
            throw new ArgumentException("Receipt files must be 10 MB or smaller.", nameof(receiptSizeBytes));
        }

        var requestId = ReimbursementRequestEntity.NewRowKey();
        var blobPath = await receiptStorage.UploadAsync(guardianId, requestId, receiptContent, receiptContentType);

        var request = new ReimbursementRequestEntity
        {
            PartitionKey = ReimbursementRequestEntity.BuildPartitionKey(guardianId),
            RowKey = requestId,
            RequestId = requestId,
            SubmitterGuardianId = guardianId,
            SubmitterName = submitterName,
            SubmitterEmail = submitterEmail,
            SubmittedOnBehalfBy = submittedOnBehalfBy,
            ScoutId = scoutId,
            ScoutName = scoutName,
            AmountRequested = amountRequested,
            Description = description,
            ReceiptBlobPath = blobPath,
            ReceiptContentType = receiptContentType,
            ReceiptOriginalFileName = receiptFileName,
            ReceiptSizeBytes = receiptSizeBytes,
            StatusValue = ReimbursementStatus.Pending,
            SubmittedUtc = timeProvider.GetUtcNow(),
        };

        try
        {
            await unitOfWork.InTransactionAsync(async () =>
            {
                await requestRepository.AddAsync(request);
                if (submittedOnBehalfBy is null)
                {
                    await auditRepository.AddAsync(
                        FinanceAuditEntityType.Reimbursement, request.RequestId, "Submitted", submitterName,
                        details: $"{amountRequested:C} — {description}");
                }
                else
                {
                    await auditRepository.AddAsync(
                        FinanceAuditEntityType.Reimbursement, request.RequestId, $"Submitted on behalf of {submitterName}", submittedOnBehalfBy,
                        details: $"{amountRequested:C} — {description}");
                }
            });
        }
        catch
        {
            // The receipt was uploaded first; don't leave it behind with no request pointing at it.
            await receiptStorage.DeleteAsync(blobPath);
            throw;
        }

        await notificationService.ReceiptSubmittedAsync(request);
        return request;
    }

    public async Task ApproveAsync(string guardianId, string requestId, string reviewedBy, string? notes)
    {
        var request = await GetOrThrowAsync(guardianId, requestId);
        request.StatusValue = ReimbursementStatus.Approved;
        request.ReviewedUtc = timeProvider.GetUtcNow();
        request.ReviewedBy = reviewedBy;
        request.ReviewNotes = notes;
        await unitOfWork.InTransactionAsync(async () =>
        {
            await requestRepository.UpdateAsync(request);
            await auditRepository.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, "Approved", reviewedBy, notes);
        });
        await notificationService.ReceiptStatusChangedAsync(request);
    }

    public async Task RejectAsync(string guardianId, string requestId, string reviewedBy, string notes)
    {
        var request = await GetOrThrowAsync(guardianId, requestId);
        request.StatusValue = ReimbursementStatus.Rejected;
        request.ReviewedUtc = timeProvider.GetUtcNow();
        request.ReviewedBy = reviewedBy;
        request.ReviewNotes = notes;
        await unitOfWork.InTransactionAsync(async () =>
        {
            await requestRepository.UpdateAsync(request);
            await auditRepository.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, "Rejected", reviewedBy, notes);
        });
        await notificationService.ReceiptStatusChangedAsync(request);
    }

    public async Task ReopenAsync(string guardianId, string requestId, string reopenedBy, string? notes)
    {
        var request = await GetOrThrowAsync(guardianId, requestId);
        if (request.StatusValue != ReimbursementStatus.Rejected)
        {
            throw new InvalidOperationException("Only rejected reimbursements can be reopened.");
        }

        request.StatusValue = ReimbursementStatus.Pending;
        request.ReviewedUtc = null;
        request.ReviewedBy = null;
        request.ReviewNotes = null;
        await unitOfWork.InTransactionAsync(async () =>
        {
            await requestRepository.UpdateAsync(request);
            await auditRepository.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, "Reopened to Pending", reopenedBy, notes);
        });
    }

    public async Task MarkPaidAsync(string guardianId, string requestId, string paidBy, string? paymentMethodNote)
    {
        var request = await GetOrThrowAsync(guardianId, requestId);
        request.StatusValue = ReimbursementStatus.Paid;
        request.PaidUtc = timeProvider.GetUtcNow();
        request.PaidBy = paidBy;
        request.PaymentMethodNote = paymentMethodNote;
        await unitOfWork.InTransactionAsync(async () =>
        {
            await requestRepository.UpdateAsync(request);
            await auditRepository.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, "Marked Paid", paidBy, paymentMethodNote);
        });
        await notificationService.ReceiptStatusChangedAsync(request);
    }

    public async Task UpdateAmountAsync(string guardianId, string requestId, decimal amount, string updatedBy)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        }

        var request = await GetOrThrowAsync(guardianId, requestId);

        var previousAmount = request.AmountRequested;
        request.AmountRequested = amount;
        await unitOfWork.InTransactionAsync(async () =>
        {
            await requestRepository.UpdateAsync(request);
            await auditRepository.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, "Amount Changed", updatedBy,
                details: $"{previousAmount:C} → {amount:C}");
        });
    }

    public async Task DeletePendingAsync(string guardianId, string requestId)
    {
        var request = await GetOrThrowAsync(guardianId, requestId);
        if (request.StatusValue != ReimbursementStatus.Pending)
        {
            throw new InvalidOperationException("Only pending receipts can be deleted.");
        }

        await unitOfWork.InTransactionAsync(async () =>
        {
            await auditRepository.AddAsync(FinanceAuditEntityType.Reimbursement, requestId, "Deleted", guardianId,
                details: "Deleted by the guardian while pending.");
            await requestRepository.DeleteAsync(request);
        });
        await receiptStorage.DeleteAsync(request.ReceiptBlobPath);
    }

    private async Task<ReimbursementRequestEntity> GetOrThrowAsync(string guardianId, string requestId) =>
        await requestRepository.GetByIdAsync(guardianId, requestId)
            ?? throw new InvalidOperationException($"Reimbursement request '{requestId}' not found.");
}
