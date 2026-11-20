using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Data.Repositories;

public interface IAdminUserRepository
{
    Task<AdminUserEntity?> GetAsync(string userKey);

    Task<IReadOnlyList<AdminUserEntity>> GetAllAsync();

    Task<int> CountWithRoleAsync(string role);

    Task AddAsync(AdminUserEntity user);

    Task UpdateAsync(AdminUserEntity user);

    /// <summary>Deletes the person and every account they've signed in with.</summary>
    Task DeleteAsync(string userKey);

    Task<AdminSignInEntity?> GetSignInAsync(string signInKey);

    /// <summary>Every account anyone has signed in with.</summary>
    Task<IReadOnlyList<AdminSignInEntity>> GetAllSignInsAsync();

    Task<IReadOnlyList<AdminSignInEntity>> GetSignInsForUserAsync(string userKey);

    /// <summary>The accounts a provider vouched for this (lower-cased) address on, most recently used first.</summary>
    Task<IReadOnlyList<AdminSignInEntity>> FindSignInsByVerifiedEmailAsync(string verifiedEmail);

    Task AddSignInAsync(AdminSignInEntity signIn);

    Task UpdateSignInAsync(AdminSignInEntity signIn);
}
