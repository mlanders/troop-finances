using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using QRCoder;

namespace BsaTroopFinances.Services;

public class GuardianAuthenticatorService(
    IGuardianRepository guardianRepository,
    IGuardianAuthenticatorRepository authenticatorRepository,
    INotificationService notificationService,
    AuthenticatorSecretProtector protector,
    TimeProvider timeProvider,
    TroopInfo troop) : IGuardianAuthenticatorService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>The one message for every "no" that could otherwise tell someone probing the page
    /// whether an email is registered or has an app linked.</summary>
    public const string MismatchMessage = "That email and code don't match. Check the code in your authenticator app and try again.";

    public Task<GuardianAuthenticatorEntity?> GetAsync(string guardianId) => authenticatorRepository.GetAsync(guardianId);

    public AuthenticatorSetup BeginSetup(string accountEmail)
    {
        var secret = Totp.GenerateSecret();
        var uri = Totp.BuildUri(troop.AuthenticatorName, accountEmail, secret);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);

        return new AuthenticatorSetup(Totp.ToBase32(secret), uri, "data:image/png;base64," + Convert.ToBase64String(png));
    }

    public async Task<bool> ConfirmSetupAsync(GuardianEntity guardian, string secretKey, string code)
    {
        byte[] secret;
        try
        {
            secret = Totp.FromBase32(secretKey);
        }
        catch (FormatException)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        if (secret.Length < 10 || !Totp.Verify(secret, code, now, out var step))
        {
            return false;
        }

        await authenticatorRepository.UpsertAsync(new GuardianAuthenticatorEntity
        {
            PartitionKey = GuardianAuthenticatorEntity.PartitionKeyValue,
            RowKey = guardian.GuardianId,
            SecretProtected = protector.Protect(secret),
            EnabledUtc = now,
            // The confirming code counts as used, so it can't also be used to sign in.
            LastUsedStep = step,
        });
        await notificationService.AuthenticatorChangedAsync(guardian, added: true);
        return true;
    }

    public async Task RemoveAsync(GuardianEntity guardian)
    {
        if (await authenticatorRepository.GetAsync(guardian.GuardianId) is null)
        {
            return;
        }

        await authenticatorRepository.DeleteAsync(guardian.GuardianId);
        await notificationService.AuthenticatorChangedAsync(guardian, added: false);
    }

    public async Task<GuardianEntity> VerifySignInAsync(string email, string code)
    {
        var guardian = string.IsNullOrWhiteSpace(email) ? null : await guardianRepository.GetByEmailAsync(email);
        var authenticator = guardian is { IsActive: true }
            ? await authenticatorRepository.GetAsync(guardian.GuardianId)
            : null;
        if (guardian is null || authenticator is null)
        {
            throw new InvalidLoginCodeException(MismatchMessage);
        }

        var now = timeProvider.GetUtcNow();
        if (authenticator.LockedUntilUtc is { } lockedUntil && now < lockedUntil)
        {
            throw new InvalidLoginCodeException(
                "Too many incorrect codes. Try again in a few minutes, or sign in with an emailed code instead.");
        }

        byte[] secret;
        try
        {
            secret = protector.Unprotect(authenticator.SecretProtected);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            // Encrypted under an earlier Auth:LoginCodePepper: the app has to be linked again.
            throw new InvalidLoginCodeException(MismatchMessage);
        }

        if (!Totp.Verify(secret, code, now, out var step))
        {
            authenticator.FailedAttempts++;
            if (authenticator.FailedAttempts >= MaxFailedAttempts)
            {
                authenticator.FailedAttempts = 0;
                authenticator.LockedUntilUtc = now + LockoutDuration;
            }

            await authenticatorRepository.UpsertAsync(authenticator);
            throw new InvalidLoginCodeException(MismatchMessage);
        }

        if (step <= authenticator.LastUsedStep)
        {
            throw new InvalidLoginCodeException("That code was already used. Wait for your app to show a new one.");
        }

        authenticator.LastUsedStep = step;
        authenticator.FailedAttempts = 0;
        authenticator.LockedUntilUtc = null;
        await authenticatorRepository.UpsertAsync(authenticator);
        return guardian;
    }
}
