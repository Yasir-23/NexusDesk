using System.Text.RegularExpressions;

namespace NexusDesk.Modules.Tenants.Domain;

public static class SubdomainValidator
{
    public static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "admin",
        "system",
        "app",
        "auth",
        "status",
        "www",
        "mail",
        "support",
        "billing",
        "docs",
        "cdn"
    };

    private static readonly Regex SubdomainRegex = new("^[a-z0-9]([a-z0-9\\-]{1,61}[a-z0-9])?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsValid(string? subdomain, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            errorMessage = "Subdomain cannot be empty.";
            return false;
        }

        var normalized = subdomain.Trim().ToLowerInvariant();

        if (normalized.Length < 3 || normalized.Length > 63)
        {
            errorMessage = "Subdomain must be between 3 and 63 characters long.";
            return false;
        }

        if (!SubdomainRegex.IsMatch(normalized))
        {
            errorMessage = "Subdomain can only contain lowercase alphanumeric characters and hyphens, and cannot start or end with a hyphen.";
            return false;
        }

        if (ReservedSubdomains.Contains(normalized))
        {
            errorMessage = $"Subdomain '{normalized}' is reserved by the platform.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
