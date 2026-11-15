using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IFundraiserEventRepository
{
    Task<IReadOnlyList<FundraiserEventEntity>> GetAllAsync();

    Task<FundraiserEventEntity?> GetByIdAsync(string eventId);

    Task AddAsync(FundraiserEventEntity fundraiserEvent);

    Task UpdateAsync(FundraiserEventEntity fundraiserEvent);
}
