using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IDonationRepository
{
    /// <summary>Every donation, newest first.</summary>
    Task<IReadOnlyList<DonationEntity>> GetAllAsync();

    /// <summary>Donations received on or after <paramref name="from"/> and before <paramref name="to"/>.</summary>
    Task<IReadOnlyList<DonationEntity>> GetReceivedBetweenAsync(DateOnly from, DateOnly to);

    Task<DonationEntity?> GetByIdAsync(string donationId);

    Task AddAsync(DonationEntity donation);

    Task UpdateAsync(DonationEntity donation);

    Task DeleteAsync(string donationId);
}
