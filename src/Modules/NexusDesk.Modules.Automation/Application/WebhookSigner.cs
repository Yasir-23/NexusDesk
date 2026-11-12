using System.Security.Cryptography;
using System.Text;

namespace NexusDesk.Modules.Automation.Application;

public static class WebhookSigner
{
    public const string DataProtectionPurpose = "NexusDesk.Webhooks.Secret.v1";

    public const string HeaderEvent = "X-NexusDesk-Event";
    public const string HeaderDelivery = "X-NexusDesk-Delivery";
    public const string HeaderTimestamp = "X-NexusDesk-Timestamp";
    public const string HeaderSignature256 = "X-NexusDesk-Signature-256";

    /// <summary>
    /// Computes the HMAC-SHA256 signature for the given webhook secret, timestamp, and raw JSON payload.
    /// Format: sha256={lowercaseHex}
    /// </summary>
    public static string ComputeSignature(string secret, string timestamp, string rawJsonBody)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes($"{timestamp}.{rawJsonBody}");

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(payloadBytes);
        var hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"sha256={hex}";
    }

    /// <summary>
    /// Constant-time verification of an incoming signature against the expected HMAC-SHA256 signature.
    /// </summary>
    public static bool VerifySignature(string secret, string timestamp, string rawJsonBody, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedSignature = ComputeSignature(secret, timestamp, rawJsonBody);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        var actualBytes = Encoding.UTF8.GetBytes(signatureHeader);

        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
