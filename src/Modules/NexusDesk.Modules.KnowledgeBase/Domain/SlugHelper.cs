using System.Text.RegularExpressions;

namespace NexusDesk.Modules.KnowledgeBase.Domain;

public static partial class SlugHelper
{
    [GeneratedRegex(@"[^a-z0-9\s-]", RegexOptions.Compiled)]
    private static partial Regex InvalidCharactersRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultipleWhitespaceRegex();

    [GeneratedRegex(@"-+", RegexOptions.Compiled)]
    private static partial Regex MultipleHyphensRegex();

    public static string Generate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"article-{Guid.NewGuid():N}"[..16];
        }

        var normalized = text.Trim().ToLowerInvariant();
        normalized = InvalidCharactersRegex().Replace(normalized, "");
        normalized = MultipleWhitespaceRegex().Replace(normalized, "-");
        normalized = MultipleHyphensRegex().Replace(normalized, "-").Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"article-{Guid.NewGuid():N}"[..16];
        }

        return normalized.Length > 150 ? normalized[..150].TrimEnd('-') : normalized;
    }

    public static string Normalize(string slug)
    {
        return Generate(slug);
    }
}
