namespace BsaTroopFinances.Services;

/// <summary>Transport only -- what gets sent, to whom, and whether it's enabled is decided by
/// <see cref="INotificationService"/>, which is what the rest of the app should call.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string plainText, string html);
}
