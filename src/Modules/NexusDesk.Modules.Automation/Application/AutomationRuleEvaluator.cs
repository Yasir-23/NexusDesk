using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.SharedKernel.Automation;

namespace NexusDesk.Modules.Automation.Application;

public sealed class AutomationRuleEvaluator : IAutomationRuleEngine
{
    public static readonly Guid AutomationActorUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const int MaxEvaluationDepth = 3;

    private static readonly AsyncLocal<ExecutionGuardState?> ExecutionGuard = new();

    private readonly AutomationDbContext _automationDb;
    private readonly ITicketAutomationActions _ticketActions;
    private readonly ILogger<AutomationRuleEvaluator> _logger;

    public AutomationRuleEvaluator(
        AutomationDbContext automationDb,
        ITicketAutomationActions ticketActions,
        ILogger<AutomationRuleEvaluator> logger)
    {
        _automationDb = automationDb;
        _ticketActions = ticketActions;
        _logger = logger;
    }

    public async Task EvaluateAndExecuteAsync(
        TicketAutomationEventContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.TenantId == Guid.Empty || context.TicketId == Guid.Empty)
        {
            return;
        }

        var isRoot = ExecutionGuard.Value == null;
        var guard = ExecutionGuard.Value ?? new ExecutionGuardState();
        if (isRoot)
        {
            ExecutionGuard.Value = guard;
        }

        if (guard.Depth >= MaxEvaluationDepth)
        {
            _logger.LogWarning(
                "Automation rule evaluation reached max depth ({MaxDepth}) for ticket {TicketId} in tenant {TenantId}; skipping to prevent infinite loop.",
                MaxEvaluationDepth,
                context.TicketId,
                context.TenantId);
            return;
        }

        guard.Depth++;
        try
        {
            var rules = await _automationDb.AutomationRules
                .AsNoTracking()
                .Where(r => r.TenantId == context.TenantId &&
                            r.IsEnabled &&
                            r.TriggerEvent == context.TriggerEvent)
                .OrderBy(r => r.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            foreach (var rule in rules)
            {
                var executionKey = $"{rule.Id:D}:{context.TicketId:D}";
                if (!guard.ExecutedRuleTicketPairs.Add(executionKey))
                {
                    continue;
                }

                if (!MatchesCondition(rule.ConditionJson, context))
                {
                    continue;
                }

                await ExecuteRuleActionAsync(rule, context, cancellationToken);
            }
        }
        finally
        {
            guard.Depth--;
            if (isRoot)
            {
                ExecutionGuard.Value = null;
            }
        }
    }

    public static bool MatchesCondition(string? conditionJson, TicketAutomationEventContext context)
    {
        if (string.IsNullOrWhiteSpace(conditionJson))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(conditionJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var expected = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();

                if (string.IsNullOrWhiteSpace(expected))
                {
                    continue;
                }

                var actual = property.Name.ToLowerInvariant() switch
                {
                    "priority" => context.Priority,
                    "oldpriority" => context.OldPriority,
                    "breachtype" => context.BreachType,
                    "ticketnumber" => context.TicketNumber,
                    "triggerevent" => context.TriggerEvent,
                    _ => null
                };

                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task ExecuteRuleActionAsync(
        AutomationRule rule,
        TicketAutomationEventContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            using var payloadDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rule.ActionPayloadJson) ? "{}" : rule.ActionPayloadJson);
            var root = payloadDoc.RootElement;

            if (string.Equals(rule.ActionType, "EscalatePriority", StringComparison.OrdinalIgnoreCase))
            {
                var targetPriority = TryGetStringProperty(root, "priority")
                    ?? TryGetStringProperty(root, "targetPriority")
                    ?? GetNextEscalatedPriority(context.Priority);

                if (!string.IsNullOrWhiteSpace(targetPriority) &&
                    !string.Equals(targetPriority, context.Priority, StringComparison.OrdinalIgnoreCase))
                {
                    await _ticketActions.EscalatePriorityAsync(
                        context.TenantId,
                        context.TicketId,
                        targetPriority,
                        AutomationActorUserId,
                        cancellationToken);
                }
            }
            else if (string.Equals(rule.ActionType, "AssignAgent", StringComparison.OrdinalIgnoreCase))
            {
                var agentIdStr = TryGetStringProperty(root, "agentId");
                if (Guid.TryParse(agentIdStr, out var agentId) && agentId != Guid.Empty)
                {
                    await _ticketActions.AssignAgentAsync(
                        context.TenantId,
                        context.TicketId,
                        agentId,
                        AutomationActorUserId,
                        cancellationToken);
                }
            }
            else if (string.Equals(rule.ActionType, "AddInternalNote", StringComparison.OrdinalIgnoreCase))
            {
                var body = TryGetStringProperty(root, "body")
                    ?? TryGetStringProperty(root, "note")
                    ?? TryGetStringProperty(root, "message")
                    ?? $"[Automation: {rule.Name}] Triggered on {context.TriggerEvent}.";

                await _ticketActions.AddInternalNoteAsync(
                    context.TenantId,
                    context.TicketId,
                    body,
                    AutomationActorUserId,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed executing automation rule {RuleId} ({ActionType}) on ticket {TicketId}.",
                rule.Id,
                rule.ActionType,
                context.TicketId);
        }
    }

    private static string? TryGetStringProperty(JsonElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
            }
        }

        return null;
    }

    private static string GetNextEscalatedPriority(string currentPriority)
    {
        return currentPriority.ToLowerInvariant() switch
        {
            "low" => "Medium",
            "medium" => "High",
            "high" => "Urgent",
            _ => "Urgent"
        };
    }

    private sealed class ExecutionGuardState
    {
        public int Depth { get; set; }
        public HashSet<string> ExecutedRuleTicketPairs { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
