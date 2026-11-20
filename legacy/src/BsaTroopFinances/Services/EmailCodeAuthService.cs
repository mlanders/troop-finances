using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;

namespace BsaTroopFinances.Services;

public class EmailCodeAuthService(
    IGuardianRepository guardianRepository,
    ILoginCodeRepository loginCodeRepository,
    INotificationService notificationService,
    IConfiguration configuration,
    TimeProvider timeProvider) : IEmailCodeAuthService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    private string Pepper => configuration["Auth:LoginCodePepper"]
        ?? throw new InvalidOperationException("Auth:LoginCodePepper is not configured.");

    public async Task RequestCodeAsync(string email)
    {
        var guardian = await guardianRepository.GetByEmailAsync(email);
        if (guardian is null || !guardian.IsActive)
        {
            // Deliberately silent: don't reveal whether this email is registered.
            return;
        }

        var normalizedEmail = GuardianEntity.NormalizeEmail(email);
        var existing = await loginCodeRepository.GetByEmailAsync(normalizedEmail);
        if (existing is not null && timeProvider.GetUtcNow() - existing.CreatedUtc < ResendCooldown)
        {
            throw new LoginCodeRateLimitedException(ResendCooldown - (timeProvider.GetUtcNow() - existing.CreatedUtc));
        }

        var code = OtpGenerator.GenerateCode();
        var now = timeProvider.GetUtcNow();

        var entity = new LoginCodeEntity
        {
            PartitionKey = LoginCodeEntity.PartitionKeyValue,
            RowKey = normalizedEmail,
            CodeHash = OtpGenerator.Hash(code, Pepper),
            ExpiresUtc = now + CodeLifetime,
            AttemptCount = 0,
            CreatedUtc = now,
        };

        await loginCodeRepository.UpsertAsync(entity);
        try
        {
            await notificationService.SendLoginCodeAsync(guardian.Email, code);
        }
        catch
        {
            await loginCodeRepository.DeleteAsync(normalizedEmail);
            throw;
        }
    }

    public async Task<GuardianEntity> VerifyCodeAsync(string email, string code)
    {
        var normalizedEmail = GuardianEntity.NormalizeEmail(email);
        var entity = await loginCodeRepository.GetByEmailAsync(normalizedEmail)
            ?? throw new InvalidLoginCodeException("That code is invalid or has expired. Request a new one.");

        if (timeProvider.GetUtcNow() > entity.ExpiresUtc)
        {
            await loginCodeRepository.DeleteAsync(normalizedEmail);
            throw new InvalidLoginCodeException("That code has expired. Request a new one.");
        }

        if (entity.AttemptCount >= MaxAttempts)
        {
            throw new InvalidLoginCodeException("Too many incorrect attempts. Request a new code.");
        }

        var expectedHash = OtpGenerator.Hash(code, Pepper);
        if (!string.Equals(expectedHash, entity.CodeHash, StringComparison.Ordinal))
        {
            entity.AttemptCount++;
            await loginCodeRepository.UpsertAsync(entity);
            throw new InvalidLoginCodeException("That code is incorrect.");
        }

        var guardian = await guardianRepository.GetByEmailAsync(normalizedEmail);
        if (guardian is null || !guardian.IsActive)
        {
            throw new InvalidLoginCodeException("That account is no longer active.");
        }

        // Single use -- consume the code so it can't be replayed.
        await loginCodeRepository.DeleteAsync(normalizedEmail);
        return guardian;
    }
}
