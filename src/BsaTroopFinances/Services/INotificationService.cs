using BsaTroopFinances.Data.Entities;

namespace BsaTroopFinances.Services;

/// <summary>
/// The one place every outgoing email goes through: checks the per-type on/off setting managed
/// at /admin/settings/email, composes the message, sends it via <see cref="IEmailSender"/>, and records
/// it in the email log.
/// </summary>
public interface INotificationService
{
    /// <summary>Always sent (can't be disabled). Throws if the send fails, so the caller can
    /// discard the code it just stored.</summary>
    Task SendLoginCodeAsync(string toEmail, string code);

    /// <summary>To the guardian, when an authenticator app is linked to or removed from their
    /// sign-in -- so a change they didn't make doesn't go unnoticed. Always sent (can't be
    /// disabled). Never throws.</summary>
    Task AuthenticatorChangedAsync(GuardianEntity guardian, bool added);

    /// <summary>To the admin notification list and the submitting guardian. Never throws.</summary>
    Task ReceiptSubmittedAsync(ReimbursementRequestEntity request);

    /// <summary>To the submitting guardian, for the request's current status (approved, rejected
    /// or paid). Never throws.</summary>
    Task ReceiptStatusChangedAsync(ReimbursementRequestEntity request);

    /// <summary>To every active guardian linked to the invoiced scout. Never throws.</summary>
    Task InvoiceIssuedAsync(InvoiceEntity invoice);

    /// <summary>To every Editor (the new one included), the committee chair and the
    /// Auth:EditorGrantNotify addresses, when someone is given Edit access. Off by default.
    /// Never throws.</summary>
    Task EditorGrantedAsync(AdminUserEntity grantee, string grantedBy);

    /// <summary>To every standing Editor, the committee chair and the Auth:EditorGrantNotify
    /// addresses (not the person themselves), when someone turns on temporary Edit. Always sent:
    /// there's no switch to turn it off. Never throws.</summary>
    Task TemporaryEditStartedAsync(AdminUserEntity person, DateTimeOffset until, string reason);
}
