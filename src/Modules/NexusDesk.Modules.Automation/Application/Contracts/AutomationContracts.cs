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

public sealed record CreateWebhookSubscriptionRequest(
    string TargetUrl,
    string Secret,
    IReadOnlyList<string> EventTypes,
    bool IsActive = true);

public sealed record WebhookSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    string TargetUrl,
    IReadOnlyList<string> EventTypes,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record OutboxMessageResponse(
    Guid Id,
    Guid TenantId,
    string EventType,
    string PayloadJson,
    string Status,
    int RetryCount,
    DateTimeOffset NextRetryAtUtc,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ProcessedAtUtc);

public sealed record DeadLetterWebhookResponse(
    Guid Id,
    Guid TenantId,
    Guid OutboxMessageId,
    Guid WebhookSubscriptionId,
    string TargetUrl,
    string EventType,
    string PayloadJson,
    int AttemptCount,
    int? LastHttpStatusCode,
    string LastError,
    DateTimeOffset FailedAtUtc);
