using Azure;
using Azure.Communication.Email;

namespace BsaTroopFinances.Services;

/// <summary>Sends email via Azure Communication Services Email. Configuration:
/// ConnectionStrings:AzureCommunicationServices and Email:SenderAddress (e.g.
/// "noreply@example.org" for your own domain, once it is verified -- see infra/DEPLOY.md).</summary>
public class AzureCommunicationServicesEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _senderAddress;

    public AzureCommunicationServicesEmailSender(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AzureCommunicationServices")
            ?? throw new InvalidOperationException("ConnectionStrings:AzureCommunicationServices is not configured.");
        _senderAddress = configuration["Email:SenderAddress"]
            ?? throw new InvalidOperationException("Email:SenderAddress is not configured.");

        _client = new EmailClient(connectionString);
    }

    public async Task SendAsync(string toEmail, string subject, string plainText, string html)
    {
        var content = new EmailContent(subject)
        {
            PlainText = plainText,
            Html = html,
        };

        var message = new EmailMessage(_senderAddress, toEmail, content);
        await _client.SendAsync(WaitUntil.Started, message);
    }
}
