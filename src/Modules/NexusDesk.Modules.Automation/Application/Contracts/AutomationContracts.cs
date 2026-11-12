namespace NexusDesk.Modules.Automation.Application.Contracts;

public sealed record UpsertSlaPolicyRequest(
    string Priority,
    int FirstResponseTargetMinutes,
    int ResolutionTargetMinutes);

public sealed record SlaPolicyResponse(
    Guid Id,
    Guid TenantId,
    string Priority,
    int FirstResponseTargetMinutes,
    int ResolutionTargetMinutes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateAutomationRuleRequest(
    string Name,
    string TriggerEvent,
    string? ConditionJson,
    string ActionType,
    string? ActionPayloadJson,
    bool IsEnabled = true);

public sealed record ToggleAutomationRuleRequest(bool? IsEnabled = null);

public sealed record AutomationRuleResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string TriggerEvent,
    string ConditionJson,
    string ActionType,
    string ActionPayloadJson,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
