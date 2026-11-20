namespace BsaTroopFinances.Services;

/// <summary>Development fallback: logs each email instead of sending it, so the parent-portal
/// login flow and notifications can be built/tested locally with no real Azure Communication
/// Services resource.</summary>
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string plainText, string html)
    {
        logger.LogWarning("DEV EMAIL (not actually sent): to {Email} -- {Subject} -- {Body}", toEmail, subject, plainText);
        return Task.CompletedTask;
    }
}
