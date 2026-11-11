using System.Security.Cryptography;
using System.Text;

namespace NexusDesk.Modules.Identity.Security;

public static class PkceHelper
{
    public static string GenerateCodeVerifier(int byteLength = 32)
    {
        var bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public static string GenerateCodeChallenge(string codeVerifier)
    {
        ArgumentNullException.ThrowIfNull(codeVerifier);

        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    public static bool VerifyCodeChallenge(string codeVerifier, string codeChallenge, string codeChallengeMethod = "S256")
        => ValidateCodeVerifier(codeVerifier, codeChallenge, codeChallengeMethod);

    public static bool ValidateCodeVerifier(string codeVerifier, string codeChallenge, string method = "S256")
    {
        if (string.IsNullOrWhiteSpace(codeVerifier) || string.IsNullOrWhiteSpace(codeChallenge))
        {
            return false;
        }

        if (!string.Equals(method, "S256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedChallenge = GenerateCodeChallenge(codeVerifier);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedChallenge),
            Encoding.UTF8.GetBytes(codeChallenge));
    }

    public static string Base64UrlEncode(byte[] input)
    {
        var base64 = Convert.ToBase64String(input);
        return base64
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
