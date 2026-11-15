using System.Security.Cryptography;
using System.Text;

namespace BsaTroopFinances.Services;

/// <summary>
/// Encrypts guardians' authenticator secrets before they're stored (AES-GCM). The key is derived
/// from Auth:LoginCodePepper -- the secret already deployed for hashing email login codes -- so no
/// new setting is needed. ASP.NET Data Protection isn't used on purpose: its keys aren't pinned
/// anywhere durable here, and losing them would silently break every linked authenticator app.
/// Rotating the pepper does the same thing deliberately: everyone's app has to be linked again.
/// </summary>
public sealed class AuthenticatorSecretProtector(IConfiguration configuration)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("BsaTroopFinances.GuardianAuthenticator.v1");

    private byte[] Key
    {
        get
        {
            var pepper = configuration["Auth:LoginCodePepper"]
                ?? throw new InvalidOperationException("Auth:LoginCodePepper is not configured.");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(pepper), 32, salt: null, info: Purpose);
        }
    }

    public string Protect(byte[] secret)
    {
        var output = new byte[NonceSize + TagSize + secret.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, secret, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Purpose);
        return Convert.ToBase64String(output);
    }

    /// <summary>Throws <see cref="CryptographicException"/> if the value was tampered with or
    /// encrypted under a different pepper.</summary>
    public byte[] Unprotect(string protectedSecret)
    {
        var input = Convert.FromBase64String(protectedSecret);
        if (input.Length <= NonceSize + TagSize)
        {
            throw new CryptographicException("The stored authenticator secret is malformed.");
        }

        var secret = new byte[input.Length - NonceSize - TagSize];
        using var aes = new AesGcm(Key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), secret, Purpose);
        return secret;
    }
}
