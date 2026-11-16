using System.Text.RegularExpressions;

namespace NexusDesk.Modules.KnowledgeBase.Services;

public sealed class PromptInjectionGuard : IPromptInjectionGuard
{
    private static readonly string[] AdversarialPhrases =
    [
        "ignore previous instructions",
        "ignore all instructions",
        "ignore prior instructions",
        "disregard all previous",
        "system prompt",
        "system instructions",
        "cross-tenant",
        "cross tenant",
        "drop table",
        "reveal secrets",
        "reveal prompt",
        "override instructions",
        "bypass guardrails",
        "you are now dan",
        "act as an unrestricted"
    ];

    private static readonly Regex AdversarialRegex = new(
        @"(ignore\s+(all\s+|previous\s+|prior\s+)*instructions)|(system\s+prompt)|(cross[\s\-_]*tenant)|(drop\s+table)|(reveal\s+(secrets|prompt))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    public bool TryValidate(string? input, out string? detectedPattern)
    {
        detectedPattern = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var normalized = input.Trim().ToLowerInvariant();

        foreach (var phrase in AdversarialPhrases)
        {
            if (normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                detectedPattern = phrase;
                return false;
            }
        }

        try
        {
            var match = AdversarialRegex.Match(input);
            if (match.Success)
            {
                detectedPattern = match.Value;
                return false;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            detectedPattern = "Regex evaluation timeout";
            return false;
        }

        return true;
    }
}
