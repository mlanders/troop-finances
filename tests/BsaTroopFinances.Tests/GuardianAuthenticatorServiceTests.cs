using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using BsaTroopFinances.Services;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>Signing in to the guardian portal with an authenticator app: linking one, using its
/// codes (once each), the lockout, and not giving away which emails are registered.</summary>
public class GuardianAuthenticatorServiceTests
{
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly InMemoryAuthenticators _authenticators = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly TestClock _clock = new();
    private readonly GuardianAuthenticatorService _service;

    private readonly GuardianEntity _maria = new() { GuardianId = "g-maria", FullName = "Maria Rivera", Email = "maria@example.com", IsActive = true };
    private readonly GuardianEntity _luis = new() { GuardianId = "g-luis", FullName = "Luis Rivera", Email = "luis@example.com", IsActive = true };

    public GuardianAuthenticatorServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:LoginCodePepper"] = "test-pepper" })
            .Build();
        _service = new GuardianAuthenticatorService(
            _guardians, _authenticators, _notifications, new AuthenticatorSecretProtector(configuration), _clock, TestTroop.Info);
        _guardians.GetByEmailAsync(Arg.Is<string>(e => GuardianEntity.NormalizeEmail(e) == "maria@example.com")).Returns(_ => _maria);
        _guardians.GetByEmailAsync(Arg.Is<string>(e => GuardianEntity.NormalizeEmail(e) == "luis@example.com")).Returns(_ => _luis);
    }

    private string CodeNow(string key, int stepOffset = 0) =>
        Totp.ComputeCode(Totp.FromBase32(key), Totp.StepAt(_clock.GetUtcNow()) + stepOffset);

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    private async Task<string> LinkMariaAsync()
    {
        var setup = _service.BeginSetup(_maria.Email);
        Assert.True(await _service.ConfirmSetupAsync(_maria, setup.SecretKey, CodeNow(setup.SecretKey)));
        return setup.SecretKey;
    }

    [Fact]
    public void Setup_offers_a_key_a_link_and_a_qr_code_without_saving_anything()
    {
        var setup = _service.BeginSetup(_maria.Email);

        Assert.Equal(32, setup.SecretKey.Length);
        Assert.Contains($"secret={setup.SecretKey}", setup.OtpAuthUri);
        Assert.StartsWith("data:image/png;base64,", setup.QrCodeDataUri);
        Assert.Empty(_authenticators.Rows);
    }

    [Fact]
    public async Task A_wrong_code_while_setting_up_saves_nothing()
    {
        var setup = _service.BeginSetup(_maria.Email);

        Assert.False(await _service.ConfirmSetupAsync(_maria, setup.SecretKey, Wrong(CodeNow(setup.SecretKey))));
        Assert.False(await _service.ConfirmSetupAsync(_maria, "not a key!", "123456"));

        Assert.Empty(_authenticators.Rows);
        await _notifications.DidNotReceiveWithAnyArgs().AuthenticatorChangedAsync(default!, default);
    }

    [Fact]
    public async Task Linking_stores_the_secret_encrypted_and_tells_the_guardian()
    {
        var key = await LinkMariaAsync();

        var row = _authenticators.Rows["g-maria"];
        Assert.DoesNotContain(key, row.SecretProtected);
        Assert.Equal(_clock.GetUtcNow(), row.EnabledUtc);
        await _notifications.Received(1).AuthenticatorChangedAsync(_maria, true);
    }

    [Fact]
    public async Task The_current_code_signs_in_and_cant_be_used_twice()
    {
        var key = await LinkMariaAsync();
        _clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        var code = CodeNow(key);

        var guardian = await _service.VerifySignInAsync(" Maria@Example.com ", code);

        Assert.Same(_maria, guardian);
        var replay = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", code));
        Assert.Contains("already used", replay.Message);
    }

    [Fact]
    public async Task The_code_used_to_finish_setup_cant_also_sign_in()
    {
        var setup = _service.BeginSetup(_maria.Email);
        var code = CodeNow(setup.SecretKey);
        await _service.ConfirmSetupAsync(_maria, setup.SecretKey, code);

        await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", code));
    }

    [Fact]
    public async Task Five_wrong_codes_lock_sign_in_for_fifteen_minutes()
    {
        var key = await LinkMariaAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));

        for (var i = 0; i < GuardianAuthenticatorService.MaxFailedAttempts; i++)
        {
            var ex = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", Wrong(CodeNow(key))));
            Assert.Equal(GuardianAuthenticatorService.MismatchMessage, ex.Message);
        }

        var locked = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", CodeNow(key)));
        Assert.Contains("Too many", locked.Message);

        _clock.Advance(GuardianAuthenticatorService.LockoutDuration + TimeSpan.FromSeconds(1));
        Assert.Same(_maria, await _service.VerifySignInAsync("maria@example.com", CodeNow(key)));
    }

    [Fact]
    public async Task Unknown_inactive_and_unlinked_emails_all_get_the_same_answer_as_a_wrong_code()
    {
        var key = await LinkMariaAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var code = CodeNow(key);

        var unknown = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("nobody@example.com", code));
        var unlinked = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("luis@example.com", code));
        var wrong = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", Wrong(code)));
        _maria.IsActive = false;
        var inactive = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", code));

        Assert.All([unknown, unlinked, wrong, inactive], ex => Assert.Equal(GuardianAuthenticatorService.MismatchMessage, ex.Message));
    }

    [Fact]
    public async Task Removing_the_app_stops_its_codes_and_tells_the_guardian()
    {
        var key = await LinkMariaAsync();

        await _service.RemoveAsync(_maria);
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(_authenticators.Rows);
        await _notifications.Received(1).AuthenticatorChangedAsync(_maria, false);
        await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifySignInAsync("maria@example.com", CodeNow(key)));
    }

    [Fact]
    public async Task Removing_when_nothing_is_linked_sends_no_email()
    {
        await _service.RemoveAsync(_luis);

        await _notifications.DidNotReceiveWithAnyArgs().AuthenticatorChangedAsync(default!, default);
    }

    private sealed class InMemoryAuthenticators : IGuardianAuthenticatorRepository
    {
        public Dictionary<string, GuardianAuthenticatorEntity> Rows { get; } = [];

        public Task<GuardianAuthenticatorEntity?> GetAsync(string guardianId) =>
            Task.FromResult(Rows.TryGetValue(guardianId, out var row) ? Copy(row) : null);

        public Task<IReadOnlySet<string>> GetGuardianIdsWithAuthenticatorAsync() =>
            Task.FromResult<IReadOnlySet<string>>(Rows.Keys.ToHashSet());

        public Task UpsertAsync(GuardianAuthenticatorEntity authenticator)
        {
            Rows[authenticator.RowKey] = Copy(authenticator);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string guardianId)
        {
            Rows.Remove(guardianId);
            return Task.CompletedTask;
        }

        private static GuardianAuthenticatorEntity Copy(GuardianAuthenticatorEntity a) => new()
        {
            PartitionKey = a.PartitionKey, RowKey = a.RowKey, SecretProtected = a.SecretProtected,
            EnabledUtc = a.EnabledUtc, LastUsedStep = a.LastUsedStep, FailedAttempts = a.FailedAttempts,
            LockedUntilUtc = a.LockedUntilUtc,
        };
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
