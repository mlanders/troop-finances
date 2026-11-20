using BsaTroopFinances.Data;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

public class DonationService(
    IDonationRepository donationRepository,
    IScoutRepository scoutRepository,
    IScoutLedgerRepository ledgerRepository,
    IFinanceAuditRepository auditRepository,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork) : IDonationService
{
    public async Task<DonationEntity> RecordAsync(DonationEntity donation, string recordedBy)
    {
        Normalize(donation);
        Validate(donation);
        donation.PartitionKey = DonationEntity.PartitionKeyValue;
        donation.RowKey = DonationEntity.NewRowKey();
        donation.CreatedUtc = timeProvider.GetUtcNow();
        donation.CreatedBy = recordedBy;

        await unitOfWork.InTransactionAsync(async () =>
        {
            await donationRepository.AddAsync(donation);
            await auditRepository.AddAsync(FinanceAuditEntityType.Donation, donation.DonationId, "Recorded", recordedBy,
                donation.Notes, $"{Describe(donation)} received {donation.ReceivedDate.UtcDateTime:MMM d, yyyy}.");
        });
        return donation;
    }

    public async Task UpdateAsync(DonationEntity donation, string changedBy)
    {
        Normalize(donation);
        Validate(donation);
        await unitOfWork.InTransactionAsync(async () =>
        {
            var credited = await CreditedTotalAsync(donation.DonationId);
            if (credited > 0 && donation.IsInKind)
            {
                throw new InvalidOperationException("This gift has been credited to scouts, so it can't be changed to goods.");
            }
            if (donation.Amount < credited)
            {
                throw new InvalidOperationException($"{credited:C} of this gift has already been credited to scouts, so the amount can't be less than that.");
            }

            await donationRepository.UpdateAsync(donation);
            await auditRepository.AddAsync(FinanceAuditEntityType.Donation, donation.DonationId, "Edited", changedBy,
                details: $"Now {Describe(donation)} received {donation.ReceivedDate.UtcDateTime:MMM d, yyyy}.");
        });
    }

    public async Task DeleteAsync(string donationId, string deletedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var donation = await donationRepository.GetByIdAsync(donationId)
                ?? throw new InvalidOperationException("Donation not found.");
            if (await CreditedTotalAsync(donationId) > 0)
            {
                throw new InvalidOperationException("This gift has been credited to scouts, so it can't be deleted.");
            }

            await donationRepository.DeleteAsync(donationId);
            await auditRepository.AddAsync(FinanceAuditEntityType.Donation, donationId, "Deleted", deletedBy,
                details: $"Deleted {Describe(donation)} received {donation.ReceivedDate.UtcDateTime:MMM d, yyyy}.");
        });
    }

    public async Task SetAcknowledgedAsync(string donationId, bool acknowledged, string changedBy)
    {
        await unitOfWork.InTransactionAsync(async () =>
        {
            var donation = await donationRepository.GetByIdAsync(donationId)
                ?? throw new InvalidOperationException("Donation not found.");
            if (donation.AcknowledgedUtc.HasValue == acknowledged)
            {
                return;
            }

            donation.AcknowledgedUtc = acknowledged ? timeProvider.GetUtcNow() : null;
            donation.AcknowledgedBy = acknowledged ? changedBy : string.Empty;
            await donationRepository.UpdateAsync(donation);
            await auditRepository.AddAsync(FinanceAuditEntityType.Donation, donationId,
                acknowledged ? "Thank-you sent" : "Thank-you marked as not sent", changedBy);
        });
    }

    public async Task CreditScoutsAsync(string donationId, IReadOnlyList<DonationCreditShare> shares, string description, string creditedBy)
    {
        if (shares.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one scout to credit.");
        }
        if (shares.Any(s => s.Amount <= 0))
        {
            throw new InvalidOperationException("Each scout's share must be more than $0.");
        }
        if (shares.Select(s => s.ScoutId).Distinct().Count() != shares.Count)
        {
            throw new InvalidOperationException("Each scout can only be listed once.");
        }

        await unitOfWork.InTransactionAsync(async () =>
        {
            var donation = await donationRepository.GetByIdAsync(donationId)
                ?? throw new InvalidOperationException("Donation not found.");
            if (donation.IsInKind)
            {
                throw new InvalidOperationException("Donated goods can't be credited to scouts' accounts.");
            }

            var remaining = donation.Amount - await CreditedTotalAsync(donationId);
            var total = shares.Sum(s => s.Amount);
            if (total > remaining)
            {
                throw new InvalidOperationException($"Only {remaining:C} of this gift is left to credit.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                description = donation.DefaultCreditDescription;
            }

            foreach (var share in shares)
            {
                var scout = await scoutRepository.GetByIdAsync(share.ScoutId)
                    ?? throw new InvalidOperationException("One of the scouts wasn't found. Reload the page and try again.");
                await ledgerRepository.AppendEntryAsync(new ScoutLedgerEntryEntity
                {
                    PartitionKey = ScoutLedgerEntryEntity.BuildPartitionKey(share.ScoutId),
                    RowKey = ScoutLedgerEntryEntity.NewRowKey(),
                    EntryTypeValue = LedgerEntryType.Credit,
                    SourceTypeValue = LedgerSourceType.Donation,
                    SourceId = donationId,
                    Amount = share.Amount,
                    Description = description.Trim(),
                    CreatedUtc = timeProvider.GetUtcNow(),
                    CreatedBy = creditedBy,
                });
                await auditRepository.AddAsync(FinanceAuditEntityType.Donation, donationId, "Credited to scout", creditedBy,
                    description.Trim(), $"{share.Amount:C} credited to {scout.FullName}.");
            }
        });
    }

    public async Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetCreditsAsync(string donationId) =>
        (await ledgerRepository.GetForSourceAsync(LedgerSourceType.Donation, donationId))
            .OrderBy(e => e.CreatedUtc)
            .ToList();

    private async Task<decimal> CreditedTotalAsync(string donationId) =>
        (await ledgerRepository.GetForSourceAsync(LedgerSourceType.Donation, donationId)).Sum(e => e.Amount);

    private static void Normalize(DonationEntity donation)
    {
        donation.DonorName = donation.DonorName.Trim();
        donation.DonorEmail = donation.DonorEmail.Trim();
        donation.DonorAddress = donation.DonorAddress.Trim();
        donation.Reference = donation.Reference.Trim();
        donation.Purpose = donation.IsRestricted ? donation.Purpose.Trim() : string.Empty;
        donation.Notes = donation.Notes.Trim();
    }

    private static void Validate(DonationEntity donation)
    {
        if (string.IsNullOrWhiteSpace(donation.DonorName))
        {
            throw new InvalidOperationException("Enter who the gift is from.");
        }
        if (donation.Amount <= 0)
        {
            throw new InvalidOperationException(donation.IsInKind
                ? "Enter an estimated value for the goods."
                : "Enter the amount given.");
        }
        if (!DonationMethod.IsValid(donation.Method))
        {
            throw new InvalidOperationException("Choose how the gift was given.");
        }
        if (DonationMethod.RequiresReference(donation.Method) && string.IsNullOrWhiteSpace(donation.Reference))
        {
            throw new InvalidOperationException(donation.IsInKind
                ? "Describe the goods that were given."
                : "Say how the gift was given.");
        }
        if (donation.IsRestricted && string.IsNullOrWhiteSpace(donation.Purpose))
        {
            throw new InvalidOperationException("Say what the donor asked the gift to be used for.");
        }
    }

    private static string Describe(DonationEntity donation) =>
        $"{donation.Amount:C} {(donation.IsInKind ? "in goods" : $"by {donation.Method.ToLowerInvariant()}")} from {donation.DonorName}" +
        (donation.IsRestricted ? $" for {donation.Purpose}" : string.Empty);
}
