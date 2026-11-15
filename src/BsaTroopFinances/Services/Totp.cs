using System.Security.Cryptography;
using System.Text;

namespace BsaTroopFinances.Services;

/// <summary>
/// Time-based one-time passwords (RFC 6238) -- the 6-digit codes authenticator apps show, changing
/// every 30 seconds. HMAC-SHA1, 30-second steps and 6 digits, the defaults every authenticator app
/// supports.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>One step either side is accepted, for a phone clock that's a little off.</summary>
    public const int AllowedDriftSteps = 1;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new 160-bit shared secret (the size RFC 4226 recommends).</summary>
    public static byte[] GenerateSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string ComputeCode(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);

        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>Checks a code against the steps around <paramref name="now"/>. On a match,
    /// <paramref name="matchedStep"/> is the step it belongs to, so the caller can refuse it if it
    /// (or a later one) was already used.</summary>
    public static bool Verify(byte[] secret, string? code, DateTimeOffset now, out long matchedStep)
    {
        matchedStep = 0;
        var trimmed = new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (trimmed.Length != Digits || !trimmed.All(char.IsAsciiDigit))
        {
            return false;
        }

        var candidate = Encoding.ASCII.GetBytes(trimmed);
        var current = StepAt(now);
        var found = false;
        for (var step = current - AllowedDriftSteps; step <= current + AllowedDriftSteps; step++)
        {
            // Every step is checked (no early exit) so timing doesn't reveal which one matched.
            var expected = Encoding.ASCII.GetBytes(ComputeCode(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, candidate) && !found)
            {
                matchedStep = step;
                found = true;
            }
        }

        return found;
    }

    /// <summary>The otpauth:// link authenticator apps read from a QR code (or open directly
    /// when tapped on a phone).</summary>
    public static string BuildUri(string issuer, string accountEmail, byte[] secret)
    {
        // Apps show this as the entry's name: "<issuer>: maria@example.com" (see TroopInfo.AuthenticatorName).
        var label = Uri.EscapeDataString($"{issuer}: {accountEmail}");
        return $"otpauth://totp/{label}?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}"
            + $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    public static string ToBase32(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    /// <summary>Decodes Base32, ignoring spaces, dashes, padding and case (people retype keys).</summary>
    public static byte[] FromBase32(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            if (raw is ' ' or '-' or '=')
            {
                continue;
            }

            var value = Base32Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (value < 0)
            {
                throw new FormatException("Not a valid Base32 key.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
