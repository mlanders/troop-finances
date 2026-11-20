using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BsaTroopFinances.Tests;

/// <summary>The receipt and invoice emails: their switches, who gets them, and what they say.</summary>
public class NotificationEmailTests
{
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IEmailSettingsRepository _settings = Substitute.For<IEmailSettingsRepository>();
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly IGuardianScoutLinkRepository _links = Substitute.For<IGuardianScoutLinkRepository>();
    private readonly IAdminUserRepository _adminUsers = Substitute.For<IAdminUserRepository>();
    private readonly EmailNotificationService _service;
    private readonly List<Sent> _sent = [];

    private sealed record Sent(string To, string Subject, string Text, string Html);

    public NotificationEmailTests()
    {
        _service = new EmailNotificationService(_sender, _settings, Substitute.For<IEmailLogRepository>(), _guardians, _links, _adminUsers,
            new ConfigurationBuilder().Build(), TestTroop.Info, TimeProvider.System, NullLogger<EmailNotificationService>.Instance);
        _settings.GetAsync().Returns(new EmailSettingsEntity());
        _adminUsers.GetAllAsync().Returns(
        [
            new AdminUserEntity { RowKey = "microsoft:pat", Name = "Pat", Email = "pat@example.com", Role = AdminAccessRole.Editor },
            new AdminUserEntity { RowKey = "microsoft:viv", Name = "Viv", Email = "viv@example.com", Role = AdminAccessRole.Viewer },
        ]);
        _guardians.GetAllAsync().Returns(
        [
            new GuardianEntity { RowKey = "maria@example.com", GuardianId = "maria", FullName = "Maria Rivera", Email = "maria@example.com", IsActive = true },
            new GuardianEntity { RowKey = "luis@example.com", GuardianId = "luis", FullName = "Luis Rivera", Email = "luis@example.com", IsActive = true },
            new GuardianEntity { RowKey = "old@example.com", GuardianId = "old", FullName = "Former Guardian", Email = "old@example.com", IsActive = false },
            new GuardianEntity { RowKey = "kim@example.com", GuardianId = "kim", FullName = "Kim Park", Email = "kim@example.com", IsActive = true },
        ]);
        _links.GetGuardiansForScoutAsync(Arg.Any<string>()).Returns([]);
        _links.GetGuardiansForScoutAsync("alex").Returns(
        [
            new GuardianScoutLinkEntity { GuardianId = "maria", ScoutId = "alex" },
            new GuardianScoutLinkEntity { GuardianId = "luis", ScoutId = "alex" },
            new GuardianScoutLinkEntity { GuardianId = "old", ScoutId = "alex" },
        ]);
        _sender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.CompletedTask)
            .AndDoes(c => _sent.Add(new(c.ArgAt<string>(0), c.ArgAt<string>(1), c.ArgAt<string>(2), c.ArgAt<string>(3))));
    }

    private static ReimbursementRequestEntity Request(string status = ReimbursementStatus.Pending, string email = "maria@example.com") => new()
    {
        RequestId = "r1", SubmitterName = "Maria Rivera", SubmitterEmail = email, ScoutName = "Alex Rivera", AmountRequested = 25m,
        Description = "Camp food", StatusValue = status,
    };

    private static InvoiceEntity ScoutInvoice(decimal total = 100m, decimal applied = 0m) => new()
    {
        PartitionKey = "alex", RowKey = "inv1", InvoiceId = "inv1", ScoutId = "alex", ScoutName = "Alex Rivera", Description = "Summer camp",
        TotalAmount = total, AmountAppliedFromAccount = applied, AmountDueFromFamily = total - applied, StatusValue = InvoiceStatus.Outstanding,
        DueDate = new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private string[] Recipients() => _sent.Select(s => s.To.ToLowerInvariant()).Order().ToArray();

    // ---- Receipt submitted

    [Fact]
    public async Task Receipt_submitted_sends_nothing_when_turned_off()
    {
        _settings.GetAsync().Returns(new EmailSettingsEntity { ReceiptSubmittedEnabled = false });

        await _service.ReceiptSubmittedAsync(Request());

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task An_editor_submitting_their_own_receipt_gets_the_confirmation_not_the_review_copy()
    {
        await _service.ReceiptSubmittedAsync(Request(email: "PAT@example.com"));

        var only = Assert.Single(_sent);
        Assert.Contains("We received your receipt", only.Text);
    }

    [Fact]
    public async Task A_receipt_entered_for_someone_says_who_entered_it()
    {
        var request = Request();
        request.SubmittedOnBehalfBy = "Pat Treasurer";

        await _service.ReceiptSubmittedAsync(request);

        Assert.All(_sent, s => Assert.Contains("Entered by Pat Treasurer on behalf of Maria Rivera.", s.Text));
    }

    [Fact]
    public async Task What_people_type_is_encoded_in_the_html_version()
    {
        var request = Request();
        request.Description = "<script>alert(1)</script> & snacks";

        await _service.ReceiptSubmittedAsync(request);

        Assert.All(_sent, s =>
        {
            Assert.DoesNotContain("<script>", s.Html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; snacks", s.Html);
        });
    }

    [Fact]
    public async Task A_failing_mail_server_doesnt_fail_the_receipt()
    {
        _sender.SendAsync(default!, default!, default!, default!).ThrowsAsyncForAnyArgs(new InvalidOperationException("SMTP down"));

        var sending = _service.ReceiptSubmittedAsync(Request());

        await sending;   // no exception
        Assert.True(sending.IsCompletedSuccessfully);
    }

    // ---- Receipt status

    [Theory]
    [InlineData(ReimbursementStatus.Approved, "has been approved", "approved")]
    [InlineData(ReimbursementStatus.Rejected, "was not approved", "rejected")]
    [InlineData(ReimbursementStatus.Paid, "has been paid", "paid")]
    public async Task A_status_change_goes_only_to_the_submitter_and_says_what_happened(string status, string summary, string inSubject)
    {
        await _service.ReceiptStatusChangedAsync(Request(status));

        var email = Assert.Single(_sent);
        Assert.Equal("maria@example.com", email.To);
        Assert.Contains(summary, email.Text);
        Assert.Contains($"reimbursement {inSubject}", email.Subject);
    }

    [Fact]
    public async Task A_rejection_includes_the_reviewers_note_and_a_payment_includes_how_it_was_paid()
    {
        var rejected = Request(ReimbursementStatus.Rejected);
        rejected.ReviewNotes = "Receipt is unreadable";
        var paid = Request(ReimbursementStatus.Paid);
        paid.ReviewNotes = "Looks right";
        paid.PaymentMethodNote = "Check 1043";

        await _service.ReceiptStatusChangedAsync(rejected);
        await _service.ReceiptStatusChangedAsync(paid);

        Assert.Contains("Note: Receipt is unreadable", _sent[0].Text);
        Assert.Contains("Note: Check 1043", _sent[1].Text);
        Assert.DoesNotContain("Looks right", _sent[1].Text);
    }

    [Fact]
    public async Task Going_back_to_pending_sends_nothing()
    {
        await _service.ReceiptStatusChangedAsync(Request(ReimbursementStatus.Pending));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Status_emails_send_nothing_when_turned_off()
    {
        _settings.GetAsync().Returns(new EmailSettingsEntity { ReceiptStatusEnabled = false });

        await _service.ReceiptStatusChangedAsync(Request(ReimbursementStatus.Approved));

        Assert.Empty(_sent);
    }

    // ---- Invoices

    [Fact]
    public async Task A_scout_invoice_goes_to_that_scouts_active_guardians()
    {
        await _service.InvoiceIssuedAsync(ScoutInvoice());

        Assert.Equal(["luis@example.com", "maria@example.com"], Recipients());
        Assert.All(_sent, s =>
        {
            Assert.Contains("invoice for Alex Rivera", s.Subject);
            Assert.Contains($"Amount due: {100m:C}", s.Text);
            Assert.Contains("Due date: May 1, 2027", s.Text);
            Assert.Contains("Applied from scout account", s.Text);
        });
    }

    [Fact]
    public async Task An_invoice_billed_to_an_adult_goes_to_that_adult_only()
    {
        var invoice = new InvoiceEntity
        {
            PartitionKey = "kim", RowKey = "inv2", InvoiceId = "inv2", GuardianId = "kim", GuardianName = "Kim Park", Description = "Summer camp, adult",
            TotalAmount = 320m, AmountDueFromFamily = 320m, StatusValue = InvoiceStatus.Outstanding,
        };

        await _service.InvoiceIssuedAsync(invoice);

        var email = Assert.Single(_sent);
        Assert.Equal("kim@example.com", email.To);
        Assert.DoesNotContain("Applied from scout account", email.Text);
        await _links.DidNotReceiveWithAnyArgs().GetGuardiansForScoutAsync(default!);
    }

    [Fact]
    public async Task An_invoice_covered_by_the_scouts_account_says_nothing_is_owed()
    {
        await _service.InvoiceIssuedAsync(ScoutInvoice(total: 60m, applied: 60m));

        Assert.All(_sent, s =>
        {
            Assert.Contains("covered in full by the scout's account", s.Text);
            Assert.DoesNotContain("Due date", s.Text);
        });
    }

    [Fact]
    public async Task A_scout_with_no_guardians_gets_no_invoice_email()
    {
        var invoice = ScoutInvoice();
        invoice.ScoutId = "orphan";

        await _service.InvoiceIssuedAsync(invoice);

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Invoice_emails_send_nothing_when_turned_off()
    {
        _settings.GetAsync().Returns(new EmailSettingsEntity { InvoiceEnabled = false });

        await _service.InvoiceIssuedAsync(ScoutInvoice());

        Assert.Empty(_sent);
    }
}
