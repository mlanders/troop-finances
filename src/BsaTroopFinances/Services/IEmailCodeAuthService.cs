using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

public interface IEmailCodeAuthService
{
    /// <summary>
    /// Requests a login code for the given email. Only sends anything if that email belongs to
    /// an active, already-registered Guardian -- there's no self-registration through this flow,
    /// and unknown emails get exactly the same (generic) outcome as known ones from the caller's
    /// point of view, so this can't be used to enumerate who's registered.
    /// </summary>
    Task RequestCodeAsync(string email);

    /// <summary>Verifies a code and, if valid, returns the matching Guardian. Throws
    /// InvalidLoginCodeException if the code is wrong/expired/missing, or the account has been
    /// locked out after too many wrong attempts.</summary>
    Task<GuardianEntity> VerifyCodeAsync(string email, string code);
}
