using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

/// <summary>What the setup page shows: the key to type in, the otpauth:// link to tap on a phone,
/// and a QR code (PNG data URI) to scan from a computer screen.</summary>
public sealed record AuthenticatorSetup(string SecretKey, string OtpAuthUri, string QrCodeDataUri);

/// <summary>
/// Signing in to the guardian portal with an authenticator app, as an alternative to an emailed
/// code. Email codes keep working whether or not an app is linked.
/// </summary>
public interface IGuardianAuthenticatorService
{
    Task<GuardianAuthenticatorEntity?> GetAsync(string guardianId);

    /// <summary>Makes a new secret for the guardian to add to their app. Nothing is saved until
    /// <see cref="ConfirmSetupAsync"/> sees a code from the app.</summary>
    AuthenticatorSetup BeginSetup(string accountEmail);

    /// <summary>Saves the authenticator if <paramref name="code"/> is valid for the secret --
    /// proof the app was set up correctly. Returns false (and saves nothing) otherwise.</summary>
    Task<bool> ConfirmSetupAsync(GuardianEntity guardian, string secretKey, string code);

    /// <summary>Unlinks the guardian's authenticator app (by the guardian, or by an admin when a
    /// phone is lost) and emails the guardian about it.</summary>
    Task RemoveAsync(GuardianEntity guardian);

    /// <summary>Returns the guardian for a valid email + authenticator code, or throws
    /// <see cref="Models.InvalidLoginCodeException"/>.</summary>
    Task<GuardianEntity> VerifySignInAsync(string email, string code);
}
