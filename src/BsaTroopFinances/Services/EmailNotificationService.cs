using System.Net;
using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;

namespace BsaTroopFinances.Services;

public class EmailNotificationService(
    IEmailSender emailSender,
    IEmailSettingsRepository settingsRepository,
    IEmailLogRepository logRepository,
    IGuardianRepository guardianRepository,
    IGuardianScoutLinkRepository linkRepository,
    IAdminUserRepository adminUserRepository,
    IConfiguration configuration,
    TroopInfo troop,
    TimeProvider timeProvider,
    ILogger<EmailNotificationService> logger) : INotificationService
{
    private string SiteUrl => troop.SiteUrl;

    public const string EditorGrantNotifyKey = "Auth:EditorGrantNotify";

    public async Task SendLoginCodeAsync(string toEmail, string code)
    {
        // The subject must never contain the code -- the subject is what the email log keeps.
        var sent = await SendAndLogAsync(
            EmailType.LoginCode, toEmail, $"Your {troop.AppName} sign-in code",
            $"Your sign-in code is {code}. It expires in 10 minutes. If you didn't request this, you can ignore this email.",
            $"<p>Your sign-in code is <strong>{code}</strong>.</p><p>It expires in 10 minutes. If you didn't request this, you can ignore this email.</p>",
            relatedEntityId: string.Empty);
        if (sent is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(sent);
        }
    }

    public async Task AuthenticatorChangedAsync(GuardianEntity guardian, bool added)
    {
        try
        {
            var subject = added
                ? $"{troop.AppName}: authenticator app added"
                : $"{troop.AppName}: authenticator app removed";
            var body = added
                ? $"An authenticator app was just linked to your {troop.AppName} sign-in. You can now sign in with the code it shows, or with an emailed code as before.\n\n"
                : $"The authenticator app linked to your {troop.AppName} sign-in was removed. You can still sign in with an emailed code.\n\n";
            body += $"If you didn't do this, sign in at {SiteUrl}portal/login with an emailed code, check Sign-in options, and let the troop treasurer know.\n";

            await SendToAllAsync(EmailType.AuthenticatorChanged, [guardian.Email], subject, body, guardian.GuardianId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't send authenticator-changed email for {GuardianId}", guardian.GuardianId);
        }
    }

    public async Task ReceiptSubmittedAsync(ReimbursementRequestEntity request)
    {
        try
        {
            var settings = await settingsRepository.GetAsync();
            if (!settings.ReceiptSubmittedEnabled)
            {
                return;
            }

            var amount = request.AmountRequested.ToString("C");
            var scoutLine = request.ScoutName is null ? string.Empty : $"Scout: {request.ScoutName}\n";
            var onBehalfLine = request.SubmittedOnBehalfBy is null
                ? string.Empty
                : $"Entered by {request.SubmittedOnBehalfBy} on behalf of {request.SubmitterName}.\n";
            var details =
                $"From: {request.SubmitterName} ({request.SubmitterEmail})\n" +
                scoutLine +
                $"Amount: {amount}\n" +
                $"Description: {request.Description}\n" +
                onBehalfLine;

            await SendToAllAsync(
                EmailType.ReceiptSubmitted, [request.SubmitterEmail],
                $"{troop.AppName}: receipt received ({amount})",
                $"We received your receipt for reimbursement. You'll get another email when it has been reviewed.\n\n{details}",
                request.RequestId);

            // Every standing Editor plus the extra addresses (which can include people who never
            // sign in); SendToAllAsync sends once per address.
            var editors = (await adminUserRepository.GetAllAsync()).Where(u => u.Role == AdminAccessRole.Editor).Select(u => u.Email);
            await SendToAllAsync(
                EmailType.ReceiptSubmitted,
                editors.Concat(settings.GetAdminRecipients()).Where(a => !SameAddress(a, request.SubmitterEmail)),
                $"{troop.AppName}: new receipt from {request.SubmitterName} ({amount})",
                $"A receipt has been submitted for reimbursement and is waiting for review.\n\n{details}",
                request.RequestId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't send receipt-submitted emails for request {RequestId}", request.RequestId);
        }
    }

    public async Task ReceiptStatusChangedAsync(ReimbursementRequestEntity request)
    {
        try
        {
            var settings = await settingsRepository.GetAsync();
            if (!settings.ReceiptStatusEnabled)
            {
                return;
            }

            var amount = request.AmountRequested.ToString("C");
            var (summary, note) = request.StatusValue switch
            {
                ReimbursementStatus.Approved => ("has been approved. You'll get another email when it has been paid.", request.ReviewNotes),
                ReimbursementStatus.Rejected => ("was not approved.", request.ReviewNotes),
                ReimbursementStatus.Paid => ("has been paid.", request.PaymentMethodNote),
                _ => (null, null),
            };
            if (summary is null)
            {
                return;
            }

            var noteLine = string.IsNullOrWhiteSpace(note) ? string.Empty : $"Note: {note}\n";
            await SendToAllAsync(
                EmailType.ReceiptStatus, [request.SubmitterEmail],
                $"{troop.AppName}: reimbursement {request.StatusValue.ToLowerInvariant()} ({amount})",
                $"Your reimbursement request {summary}\n\n" +
                $"Amount: {amount}\n" +
                $"Description: {request.Description}\n" +
                noteLine,
                request.RequestId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't send receipt-status email for request {RequestId}", request.RequestId);
        }
    }

    public async Task InvoiceIssuedAsync(InvoiceEntity invoice)
    {
        try
        {
            var settings = await settingsRepository.GetAsync();
            if (!settings.InvoiceEnabled)
            {
                return;
            }

            // An invoice billed to an adult goes to that adult; a scout's goes to the scout's guardians.
            var guardianIds = invoice.IsForGuardian
                ? [invoice.GuardianId!]
                : (await linkRepository.GetGuardiansForScoutAsync(invoice.ScoutId)).Select(l => l.GuardianId).ToHashSet();
            if (guardianIds.Count == 0)
            {
                return;
            }

            var recipients = (await guardianRepository.GetAllAsync())
                .Where(g => g.IsActive && guardianIds.Contains(g.GuardianId))
                .Select(g => g.Email);

            var dueLine = invoice.AmountDueFromFamily > 0
                ? $"Amount due: {invoice.AmountDueFromFamily:C}\n" +
                  (invoice.DueDate.HasValue ? $"Due date: {invoice.DueDate.Value.UtcDateTime:MMM d, yyyy}\n" : string.Empty)
                : "Amount due: $0.00 -- this invoice was covered in full by the scout's account. Nothing further is owed.\n";

            await SendToAllAsync(
                EmailType.Invoice, recipients,
                $"{troop.AppName}: invoice for {invoice.BilledTo} ({invoice.TotalAmount:C})",
                $"An invoice has been issued for {invoice.BilledTo}.\n\n" +
                $"Description: {invoice.Description}\n" +
                $"Total: {invoice.TotalAmount:C}\n" +
                (invoice.IsForGuardian ? string.Empty : $"Applied from scout account: {invoice.AmountAppliedFromAccount:C}\n") +
                dueLine,
                invoice.InvoiceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't send invoice emails for invoice {InvoiceId}", invoice.InvoiceId);
        }
    }

    public async Task EditorGrantedAsync(AdminUserEntity grantee, string grantedBy)
    {
        try
        {
            var settings = await settingsRepository.GetAsync();
            if (!settings.EditorGrantedEnabled)
            {
                return;
            }

            var people = await adminUserRepository.GetAllAsync();
            var recipients = people.Where(u => u.Role == AdminAccessRole.Editor).Select(u => u.Email)
                .Append(grantee.Email)
                .Concat(people.Where(u => u.IsCommitteeChair).Select(u => u.Email))
                .Concat(AdminUserService.ConfiguredAddresses(configuration, EditorGrantNotifyKey));

            var who = string.IsNullOrWhiteSpace(grantee.Email) ? grantee.Name : $"{grantee.Name} ({grantee.Email})";
            await SendToAllAsync(
                EmailType.EditorGranted, recipients,
                $"{troop.AppName}: {grantee.Name} was given Edit access",
                $"{who} was given Edit access to {troop.AppName} by {grantedBy}.\n\n" +
                "Edit access can issue invoices, record payments, approve reimbursements and change who has access. " +
                $"If this wasn't expected, an Editor can change it at {SiteUrl}admin/settings/access.\n",
                grantee.RowKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't send edit-access-granted emails for {UserKey}", grantee.RowKey);
        }
    }

    /// <summary>No switch to turn this off: it's the check on people giving themselves Edit.</summary>
    public async Task TemporaryEditStartedAsync(AdminUserEntity person, DateTimeOffset until, string reason)
    {
        try
        {
            var people = await adminUserRepository.GetAllAsync();
            var recipients = people.Where(u => u.Role == AdminAccessRole.Editor).Select(u => u.Email)
                .Concat(people.Where(u => u.IsCommitteeChair).Select(u => u.Email))
                .Concat(AdminUserService.ConfiguredAddresses(configuration, EditorGrantNotifyKey))
                .Where(e => !string.Equals(e?.Trim(), person.Email.Trim(), StringComparison.OrdinalIgnoreCase));

            var who = string.IsNullOrWhiteSpace(person.Email) ? person.Name : $"{person.Name} ({person.Email})";
            var untilText = until.ToLocalTime().ToString("dddd, MMM d 'at' h:mm tt");
            await SendToAllAsync(
                EmailType.TemporaryEdit, recipients,
                $"{troop.AppName}: {person.Name} turned on Edit access",
                $"{who} turned on Edit access to {troop.AppName} until {untilText}.\n\n" +
                $"Reason given: {reason}\n\n" +
                "Their access goes back to Read only on its own after that. Everything they change is in Activity History. " +
                $"If this wasn't expected, an Editor can stop them from turning on Edit at {SiteUrl}admin/settings/access.\n",
                person.RowKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't send temporary-edit emails for {UserKey}", person.RowKey);
        }
    }

    private async Task SendToAllAsync(string emailType, IEnumerable<string> recipients, string subject, string plainText, string relatedEntityId)
    {
        // Bodies are composed as plain text (which includes user-entered descriptions and notes),
        // so the HTML version is just that text encoded, with line breaks preserved.
        var html = $"<p>{WebUtility.HtmlEncode(plainText.TrimEnd()).Replace("\n", "<br />")}</p>";
        foreach (var recipient in recipients
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .DistinctBy(GuardianEntity.NormalizeEmail))
        {
            await SendAndLogAsync(emailType, recipient, subject, plainText, html, relatedEntityId);
        }
    }

    /// <summary>Returns the send failure (already logged as Failed), or null when sent.</summary>
    private async Task<Exception?> SendAndLogAsync(string emailType, string toEmail, string subject, string plainText, string html, string relatedEntityId)
    {
        Exception? failure = null;
        try
        {
            // Every email, of every type, gets the same footer.
            await emailSender.SendAsync(
                toEmail, subject,
                $"{plainText.TrimEnd()}\n\nSent from {troop.AppName}\n{SiteUrl}",
                $"{html}<p>Sent from {troop.AppName}<br /><a href=\"{SiteUrl}\">{SiteUrl}</a></p>");
        }
        catch (Exception ex)
        {
            failure = ex;
            logger.LogError(ex, "Couldn't send {EmailType} email to {Email}", emailType, toEmail);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            await logRepository.AddAsync(new EmailLogEntity
            {
                PartitionKey = EmailLogEntity.BuildPartitionKey(now),
                RowKey = EmailLogEntity.NewRowKey(now),
                EmailType = emailType,
                ToEmail = GuardianEntity.NormalizeEmail(toEmail),
                Subject = subject,
                Status = failure is null ? EmailLogStatus.Sent : EmailLogStatus.Failed,
                Error = failure?.Message ?? string.Empty,
                SentUtc = now,
                RelatedEntityId = relatedEntityId,
            });
        }
        catch (Exception ex)
        {
            // A logging failure must not turn a successfully sent email into an error.
            logger.LogError(ex, "Couldn't record {EmailType} email to {Email} in the email log", emailType, toEmail);
        }

        return failure;
    }

    private static bool SameAddress(string a, string b) =>
        GuardianEntity.NormalizeEmail(a) == GuardianEntity.NormalizeEmail(b);
}
