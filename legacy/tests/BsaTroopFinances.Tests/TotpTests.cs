using System.Text;
using BsaTroopFinances.Services;

namespace BsaTroopFinances.Tests;

/// <summary>The authenticator-app codes: they must match what real apps compute (RFC 6238), allow a
/// little clock drift, and the setup key must survive being retyped.</summary>
public class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    // RFC 6238 appendix B (SHA-1), last six digits of the eight-digit values.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_rfc_test_vectors(long unixSeconds, string expected)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        Assert.Equal(expected, Totp.ComputeCode(RfcSecret, Totp.StepAt(at)));
        Assert.True(Totp.Verify(RfcSecret, expected, at, out var step));
        Assert.Equal(Totp.StepAt(at), step);
    }

    [Fact]
    public void One_step_of_drift_either_way_is_accepted_but_two_is_not()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);

        Assert.True(Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step - 1), now, out _));
        Assert.True(Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step + 1), now, out _));
        Assert.False(Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step - 2), now, out _));
        Assert.False(Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step + 2), now, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Malformed_codes_are_rejected(string? code)
    {
        Assert.False(Totp.Verify(RfcSecret, code, DateTimeOffset.UtcNow, out _));
    }

    [Fact]
    public void Spaces_typed_in_a_code_are_ignored()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(59);

        Assert.True(Totp.Verify(RfcSecret, "287 082", now, out _));
    }

    [Theory]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_rfc_4648(string text, string expected)
    {
        Assert.Equal(expected, Totp.ToBase32(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void A_setup_key_round_trips_even_retyped_in_lowercase_with_spaces()
    {
        var secret = Totp.GenerateSecret();
        var key = Totp.ToBase32(secret);
        var retyped = string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(c => new string(c)));

        Assert.Equal(secret, Totp.FromBase32(retyped));
        Assert.Throws<FormatException>(() => Totp.FromBase32("not base32!"));
    }

    [Fact]
    public void The_setup_link_names_the_troop_and_the_account()
    {
        var uri = Totp.BuildUri(TestTroop.Info.AuthenticatorName, "maria@example.com", RfcSecret);

        Assert.StartsWith("otpauth://totp/T52%20Finance%20Guardian%3A%20maria%40example.com?secret=", uri);
        Assert.Contains($"secret={Totp.ToBase32(RfcSecret)}&", uri);
        Assert.Contains("issuer=T52%20Finance%20Guardian", uri);
    }
}
