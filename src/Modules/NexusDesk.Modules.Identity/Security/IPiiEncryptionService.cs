using Microsoft.AspNetCore.DataProtection;

namespace NexusDesk.Modules.Identity.Security;

public interface IPiiEncryptionService
{
    string? Encrypt(string? plainText);
    string? Decrypt(string? cipherText);
}

public sealed class PiiEncryptionService : IPiiEncryptionService
{
    public const string DataProtectionPurpose = "NexusDesk.Identity.PII.v1";
    private readonly IDataProtector _protector;

    public PiiEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
    }

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return plainText;
        }

        return _protector.Protect(plainText);
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return cipherText;
        }

        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch
        {
            // If already plaintext or protection key mismatch, return as-is
            return cipherText;
        }
    }
}
