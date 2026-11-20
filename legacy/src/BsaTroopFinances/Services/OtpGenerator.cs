using System.Security.Cryptography;
using System.Text;

namespace BsaTroopFinances.Services;

/// <summary>Generates and hashes the 6-digit email login codes. The code itself is never
/// persisted -- only a keyed hash (LoginCodeEntity.CodeHash) -- so a Table Storage read (or
/// leak) can't itself grant access to the portal.</summary>
public static class OtpGenerator
{
    public static string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Hash(string code, string pepper)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
