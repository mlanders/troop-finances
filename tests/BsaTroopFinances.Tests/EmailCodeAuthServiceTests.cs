using BsaTroopFinances.Data.Entities;
using BsaTroopFinances.Data.Repositories;
using BsaTroopFinances.Models;
using BsaTroopFinances.Services;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BsaTroopFinances.Tests;

/// <summary>The guardian portal's emailed one-time codes: who gets one, how long it lasts, and how
/// many tries it allows.</summary>
public class EmailCodeAuthServiceTests
{
    private readonly IGuardianRepository _guardians = Substitute.For<IGuardianRepository>();
    private readonly InMemoryLoginCodes _codes = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly TestClock _clock = new();
    private readonly EmailCodeAuthService _service;
    private string? _sentCode;

    private readonly GuardianEntity _maria = new() { Email = "maria@example.com", IsActive = true };

    public EmailCodeAuthServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:LoginCodePepper"] = "test-pepper" })
            .Build();
        _service = new EmailCodeAuthService(_guardians, _codes, _notifications, configuration, _clock);
        _guardians.GetByEmailAsync(Arg.Is<string>(e => GuardianEntity.NormalizeEmail(e) == "maria@example.com")).Returns(_ => _maria);
        _notifications.SendLoginCodeAsync(Arg.Any<string>(), Arg.Do<string>(code => _sentCode = code)).Returns(Task.CompletedTask);
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task A_registered_guardian_gets_a_six_digit_code_that_isnt_stored_as_is()
    {
        await _service.RequestCodeAsync(" Maria@Example.com ");

        Assert.Matches("^[0-9]{6}$", _sentCode);
        var stored = _codes.Rows["maria@example.com"];
        Assert.NotEqual(_sentCode, stored.CodeHash);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(10), stored.ExpiresUtc);
    }

    [Fact]
    public async Task An_unknown_or_inactive_email_gets_nothing_and_no_hint()
    {
        await _service.RequestCodeAsync("nobody@example.com");
        _maria.IsActive = false;
        await _service.RequestCodeAsync("maria@example.com");

        Assert.Empty(_codes.Rows);
        await _notifications.DidNotReceiveWithAnyArgs().SendLoginCodeAsync(default!, default!);
    }

    [Fact]
    public async Task Asking_again_within_a_minute_is_refused_and_after_it_is_allowed()
    {
        await _service.RequestCodeAsync("maria@example.com");
        _clock.Advance(TimeSpan.FromSeconds(30));

        var refused = await Assert.ThrowsAsync<LoginCodeRateLimitedException>(() => _service.RequestCodeAsync("maria@example.com"));
        Assert.Equal(TimeSpan.FromSeconds(30), refused.RetryAfter);

        _clock.Advance(TimeSpan.FromSeconds(31));
        await _service.RequestCodeAsync("maria@example.com");
        await _notifications.Received(2).SendLoginCodeAsync("maria@example.com", Arg.Any<string>());
    }

    [Fact]
    public async Task If_the_email_cant_be_sent_the_code_is_removed()
    {
        _notifications.SendLoginCodeAsync(Arg.Any<string>(), Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("mail down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RequestCodeAsync("maria@example.com"));

        Assert.Empty(_codes.Rows);
    }

    [Fact]
    public async Task The_right_code_signs_in_once_only()
    {
        await _service.RequestCodeAsync("maria@example.com");

        Assert.Same(_maria, await _service.VerifyCodeAsync("MARIA@example.com", _sentCode!));
        await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifyCodeAsync("maria@example.com", _sentCode!));
    }

    [Fact]
    public async Task A_code_expires_after_ten_minutes()
    {
        await _service.RequestCodeAsync("maria@example.com");
        _clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        var expired = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifyCodeAsync("maria@example.com", _sentCode!));

        Assert.Contains("expired", expired.Message);
        Assert.Empty(_codes.Rows);
    }

    [Fact]
    public async Task Five_wrong_tries_lock_the_code_even_for_the_right_one()
    {
        await _service.RequestCodeAsync("maria@example.com");
        var wrong = WrongCode(_sentCode!);
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifyCodeAsync("maria@example.com", wrong));
        }

        var locked = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifyCodeAsync("maria@example.com", _sentCode!));

        Assert.Contains("Too many", locked.Message);
        Assert.Equal(5, _codes.Rows["maria@example.com"].AttemptCount);
    }

    [Fact]
    public async Task A_guardian_deactivated_after_asking_cant_sign_in()
    {
        await _service.RequestCodeAsync("maria@example.com");
        _maria.IsActive = false;

        var refused = await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifyCodeAsync("maria@example.com", _sentCode!));

        Assert.Contains("no longer active", refused.Message);
    }

    [Fact]
    public async Task No_code_requested_means_nothing_to_verify()
    {
        await Assert.ThrowsAsync<InvalidLoginCodeException>(() => _service.VerifyCodeAsync("maria@example.com", "123456"));
    }

    [Fact]
    public void Codes_are_six_digits_and_hash_the_same_way_every_time()
    {
        Assert.All(Enumerable.Range(0, 50).Select(_ => OtpGenerator.GenerateCode()), code => Assert.Matches("^[0-9]{6}$", code));
        Assert.Equal(OtpGenerator.Hash("012345", "pepper"), OtpGenerator.Hash("012345", "pepper"));
        Assert.NotEqual(OtpGenerator.Hash("012345", "pepper"), OtpGenerator.Hash("012345", "other pepper"));
    }

    private sealed class InMemoryLoginCodes : ILoginCodeRepository
    {
        public Dictionary<string, LoginCodeEntity> Rows { get; } = [];

        public Task<LoginCodeEntity?> GetByEmailAsync(string email) =>
            Task.FromResult(Rows.TryGetValue(email, out var row) ? Copy(row) : null);

        public Task UpsertAsync(LoginCodeEntity code)
        {
            Rows[code.RowKey] = Copy(code);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string email)
        {
            Rows.Remove(email);
            return Task.CompletedTask;
        }

        private static LoginCodeEntity Copy(LoginCodeEntity c) => new()
        {
            PartitionKey = c.PartitionKey, RowKey = c.RowKey, CodeHash = c.CodeHash,
            ExpiresUtc = c.ExpiresUtc, AttemptCount = c.AttemptCount, CreatedUtc = c.CreatedUtc,
        };
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
