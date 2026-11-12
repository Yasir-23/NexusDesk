namespace NexusDesk.SharedKernel.Automation;

/// <summary>
/// SLA target durations in minutes for a ticket priority.
/// </summary>
public readonly record struct SlaPolicyTargets(
    int FirstResponseTargetMinutes,
    int ResolutionTargetMinutes);

/// <summary>
/// Provides tenant-configured SLA policy targets with fallback to null when none is configured.
/// </summary>
public interface ISlaPolicyProvider
{
    Task<SlaPolicyTargets?> GetPolicyTargetsAsync(
        Guid tenantId,
        string priority,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Integration event context passed to the automation rule engine when ticket lifecycle events occur.
/// </summary>
public sealed record TicketAutomationEventContext(
    Guid TicketId,
    Guid TenantId,
    string TicketNumber,
    string TriggerEvent,
    string Priority,
    string? OldPriority = null,
    string? BreachType = null,
    Guid? PerformedByUserId = null,
    DateTimeOffset? OccurredAtUtc = null);

/// <summary>
/// Evaluates and executes enabled automation rules for a tenant event context.
/// </summary>
public interface IAutomationRuleEngine
{
    Task EvaluateAndExecuteAsync(
        TicketAutomationEventContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Public application contract for automation actions executed against tickets.
/// </summary>
public interface ITicketAutomationActions
{
    Task<bool> EscalatePriorityAsync(
        Guid tenantId,
        Guid ticketId,
        string targetPriority,
        Guid performedByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> AssignAgentAsync(
        Guid tenantId,
        Guid ticketId,
        Guid agentId,
        Guid performedByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> AddInternalNoteAsync(
        Guid tenantId,
        Guid ticketId,
        string noteBody,
        Guid authorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evaluates active tickets for a single tenant and marks any breached SLAs.
/// </summary>
public interface ITicketSlaBreachEvaluator
{
    Task<int> EvaluateTenantBreachesAsync(
        Guid tenantId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
