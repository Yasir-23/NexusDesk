namespace NexusDesk.Modules.KnowledgeBase.Services;

public interface IPromptInjectionGuard
{
    bool TryValidate(string? input, out string? detectedPattern);
}
