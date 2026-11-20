using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>The "Edit access granted" email: off by default, and to whom when it's on.</summary>
public class EditorGrantedEmailTests
{
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IEmailSettingsRepository _settings = Substitute.For<IEmailSettingsRepository>();
    private readonly IAdminUserRepository _adminUsers = Substitute.For<IAdminUserRepository>();
    private readonly EmailNotificationService _service;

    private readonly AdminUserEntity _jordan = new() { RowKey = "google:1", Name = "Jordan", Email = "jordan@example.com", Role = AdminAccessRole.Editor };

    public EditorGrantedEmailTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:EditorGrantNotify:0"] = "steve@example.org" })
            .Build();
        _service = new EmailNotificationService(_sender, _settings, Substitute.For<IEmailLogRepository>(),
            Substitute.For<IGuardianRepository>(), Substitute.For<IGuardianScoutLinkRepository>(), _adminUsers,
            configuration, TestTroop.Info, TimeProvider.System, NullLogger<EmailNotificationService>.Instance);

        _adminUsers.GetAllAsync().Returns(
        [
            new AdminUserEntity { RowKey = "microsoft:pat", Name = "Pat", Email = "pat@example.com", Role = AdminAccessRole.Editor },
            new AdminUserEntity { RowKey = "microsoft:viv", Name = "Viv", Email = "viv@example.com", Role = AdminAccessRole.Viewer },
            new AdminUserEntity { RowKey = "google:chair", Name = "Chris", Email = "chair@example.com", Role = AdminAccessRole.Viewer, IsCommitteeChair = true },
            new AdminUserEntity { RowKey = "google:steve", Name = "Steve", Email = "STEVE@example.org", Role = AdminAccessRole.Editor },
            _jordan,
        ]);
    }

    [Fact]
    public void Is_off_until_turned_on() => Assert.False(new EmailSettingsEntity().EditorGrantedEnabled);

    [Fact]
    public async Task Sends_nothing_while_turned_off()
    {
        _settings.GetAsync().Returns(new EmailSettingsEntity());

        await _service.EditorGrantedAsync(_jordan, "Pat");

        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Goes_once_each_to_every_editor_the_chair_and_the_configured_addresses()
    {
        _settings.GetAsync().Returns(new EmailSettingsEntity { EditorGrantedEnabled = true });
        var sentTo = new List<string>();
        await _sender.SendAsync(Arg.Do<string>(sentTo.Add), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());

        await _service.EditorGrantedAsync(_jordan, "Pat");

        Assert.Equal(
            ["chair@example.com", "jordan@example.com", "pat@example.com", "steve@example.org"],
            sentTo.Select(a => a.ToLowerInvariant()).Order());
    }

    [Fact]
    public async Task Receipt_submitted_goes_to_the_submitter_every_editor_and_the_extra_addresses()
    {
        _settings.GetAsync().Returns(new EmailSettingsEntity { AdminRecipients = "bookkeeper@example.com\npat@example.com" });

        await _service.ReceiptSubmittedAsync(new ReimbursementRequestEntity
        {
            RequestId = "r1", SubmitterName = "Maria", SubmitterEmail = "maria@example.com", AmountRequested = 25m, Description = "Camp food",
        });

        Assert.Equal(
            ["bookkeeper@example.com", "jordan@example.com", "maria@example.com", "pat@example.com", "steve@example.org"],
            _sender.ReceivedCalls().Select(c => ((string)c.GetArguments()[0]!).ToLowerInvariant()).Order());
    }

    [Fact]
    public async Task The_temporary_edit_alert_always_goes_to_editors_the_chair_and_configured_addresses_but_not_the_person()
    {
        // Every switch off: the alert has none of its own and ignores the others.
        _settings.GetAsync().Returns(new EmailSettingsEntity
        {
            ReceiptSubmittedEnabled = false, ReceiptStatusEnabled = false, InvoiceEnabled = false, EditorGrantedEnabled = false,
        });
        var steve = new AdminUserEntity { RowKey = "google:steve", Name = "Steve", Email = "steve@example.org", Role = AdminAccessRole.Viewer, IsGlobalAdmin = true };

        await _service.TemporaryEditStartedAsync(steve, DateTimeOffset.UtcNow.AddHours(2), "Fix the October dues invoice");

        var sent = _sender.ReceivedCalls().Select(c => c.GetArguments()).ToList();
        Assert.Equal(["chair@example.com", "jordan@example.com", "pat@example.com"], sent.Select(a => ((string)a[0]!).ToLowerInvariant()).Order());
        Assert.All(sent, a => Assert.Contains(a.OfType<string>(), s => s.Contains("Fix the October dues invoice")));
    }
}
