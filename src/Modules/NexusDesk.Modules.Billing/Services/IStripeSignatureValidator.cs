using System.Security.Cryptography;
using System.Text;

namespace NexusDesk.Modules.Billing.Services;

public interface IStripeSignatureValidator
{
    bool ValidateSignature(string? signatureHeader, string rawPayload, string webhookSecret);
}

public sealed class StripeSignatureValidator : IStripeSignatureValidator
{
    public bool ValidateSignature(string? signatureHeader, string rawPayload, string webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(webhookSecret))
        {
            return false;
        }

        string? timestamp = null;
        var signatures = new List<string>();

        var parts = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kvp = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kvp.Length != 2)
            {
                continue;
            }

            if (kvp[0].Equals("t", StringComparison.OrdinalIgnoreCase))
            {
                timestamp = kvp[1];
            }
            else if (kvp[0].Equals("v1", StringComparison.OrdinalIgnoreCase))
            {
                signatures.Add(kvp[1]);
            }
        }

        if (string.IsNullOrWhiteSpace(timestamp) || signatures.Count == 0)
        {
            return false;
        }

        var signedPayload = $"{timestamp}.{rawPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expectedHex = Convert.ToHexStringLower(hashBytes);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex);

        foreach (var sig in signatures)
        {
            var sigBytes = Encoding.UTF8.GetBytes(sig.Trim().ToLowerInvariant());
            if (sigBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, sigBytes))
            {
                return true;
            }
        }

        return false;
    }
}
