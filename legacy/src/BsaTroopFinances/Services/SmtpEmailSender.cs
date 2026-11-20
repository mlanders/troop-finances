using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BsaTroopFinances.Services;

/// <summary>Settings for <see cref="SmtpEmailSender"/> (the Email:Smtp section).</summary>
public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "";

    /// <summary>587 (STARTTLS) by default; 465 means TLS from the start.</summary>
    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>"Auto" (the default: TLS on 465, STARTTLS where offered elsewhere), "StartTls",
    /// "SslOnConnect", or "None" -- only for a local test server such as Mailpit.</summary>
    public string Security { get; set; } = "Auto";
}

/// <summary>
/// Sends email through any SMTP server -- Email:Provider "Smtp" -- so the app can run without
/// Azure Communication Services. Every transactional email service offers SMTP (Resend, SendGrid,
/// Brevo, Mailgun, Amazon SES, Postmark), as do Google Workspace and Microsoft 365. Email:SenderAddress
/// must be an address that service is allowed to send from (a verified domain), or messages
/// bounce or land in spam. One connection per message: the app sends a handful a day.
/// </summary>
public class SmtpEmailSender(SmtpEmailOptions options, string senderAddress) : IEmailSender
{
    public SmtpEmailSender(IConfiguration configuration)
        : this(
            configuration.GetSection("Email:Smtp").Get<SmtpEmailOptions>() is { Host.Length: > 0 } smtp
                ? smtp
                : throw new InvalidOperationException("Email:Provider is Smtp, but Email:Smtp:Host isn't set."),
            configuration["Email:SenderAddress"] is { Length: > 0 } sender
                ? sender
                : throw new InvalidOperationException("Email:SenderAddress is not configured."))
    {
    }

    public async Task SendAsync(string toEmail, string subject, string plainText, string html)
    {
        using var message = BuildMessage(senderAddress, toEmail, subject, plainText, html);
        using var client = new SmtpClient();
        await client.ConnectAsync(options.Host, options.Port, SecurityOption(options));
        if (!string.IsNullOrEmpty(options.Username))
        {
            await client.AuthenticateAsync(options.Username, options.Password ?? "");
        }

        await client.SendAsync(new SendRequest(message));
        await client.DisconnectAsync(quit: true);
    }

    /// <summary>A plain-text and HTML alternative of the same message.</summary>
    public static MimeMessage BuildMessage(string from, string to, string subject, string plainText, string html)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { TextBody = plainText, HtmlBody = html }.ToMessageBody();
        return message;
    }

    public static SecureSocketOptions SecurityOption(SmtpEmailOptions options) => options.Security.ToLowerInvariant() switch
    {
        "auto" => SecureSocketOptions.Auto,
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" or "ssl" or "tls" => SecureSocketOptions.SslOnConnect,
        "none" => SecureSocketOptions.None,
        _ => throw new InvalidOperationException($"Email:Smtp:Security '{options.Security}' isn't known. Use Auto, StartTls, SslOnConnect or None."),
    };
}
