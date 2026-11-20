using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

/// <summary>
/// Records gifts to the troop and credits them to scouts. Every change is audited. Rule
/// violations throw InvalidOperationException with a message fit to show the treasurer.
/// </summary>
public interface IDonationService
{
    /// <summary>Saves a new donation, filling in its id and created fields.</summary>
    Task<DonationEntity> RecordAsync(DonationEntity donation, string recordedBy);

    /// <summary>Saves changes to a donation. The amount can't drop below what's already been
    /// credited to scouts, and a credited gift can't become goods.</summary>
    Task UpdateAsync(DonationEntity donation, string changedBy);

    /// <summary>Deletes a donation that hasn't been credited to any scout.</summary>
    Task DeleteAsync(string donationId, string deletedBy);

    /// <summary>Marks the thank-you as sent, or (acknowledged false) as still owed.</summary>
    Task SetAcknowledgedAsync(string donationId, bool acknowledged, string changedBy);

    /// <summary>
    /// Credits part of a money gift to scouts' accounts, all in one transaction: either every
    /// scout is credited or none is. The total can't be more than what's left of the gift.
    /// </summary>
    Task CreditScoutsAsync(string donationId, IReadOnlyList<DonationCreditShare> shares, string description, string creditedBy);

    /// <summary>The ledger entries crediting this donation to scouts, oldest first.</summary>
    Task<IReadOnlyList<ScoutLedgerEntryEntity>> GetCreditsAsync(string donationId);
}

public record DonationCreditShare(string ScoutId, decimal Amount);
