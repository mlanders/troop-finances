using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface ILoginCodeRepository
{
    Task<LoginCodeEntity?> GetByEmailAsync(string email);

    /// <summary>Replaces any existing pending code for this email -- only one active code per
    /// email at a time.</summary>
    Task UpsertAsync(LoginCodeEntity code);

    Task DeleteAsync(string email);
}
