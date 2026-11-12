using System.Text.Json;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Domain;

public sealed class AutomationRule : Entity<Guid>, ITenantScoped
{
    public const int MaxNameLength = 150;

    public static readonly IReadOnlySet<string> SupportedTriggerEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "TicketCreated",
        "TicketSlaBreached",
        "TicketPriorityChanged"
    };

    public static readonly IReadOnlySet<string> SupportedActionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "EscalatePriority",
        "AssignAgent",
        "AddInternalNote"
    };

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string TriggerEvent { get; private set; }
    public string ConditionJson { get; private set; }
    public string ActionType { get; private set; }
    public string ActionPayloadJson { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    private AutomationRule(
        Guid id,
        Guid tenantId,
        string name,
        string triggerEvent,
        string conditionJson,
        string actionType,
        string actionPayloadJson,
        bool isEnabled,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        TriggerEvent = triggerEvent;
        ConditionJson = conditionJson;
        ActionType = actionType;
        ActionPayloadJson = actionPayloadJson;
        IsEnabled = isEnabled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    private AutomationRule() : base()
    {
        Name = string.Empty;
        TriggerEvent = string.Empty;
        ConditionJson = "{}";
        ActionType = string.Empty;
        ActionPayloadJson = "{}";
    }

    public AutomationRule(Guid id, Guid tenantId, string name, string triggerEvent, bool isEnabled = true) : base(id)
    {
        TenantId = tenantId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TriggerEvent = triggerEvent ?? throw new ArgumentNullException(nameof(triggerEvent));
        ConditionJson = "{}";
        ActionType = "EscalatePriority";
        ActionPayloadJson = "{}";
        IsEnabled = isEnabled;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public static Result<AutomationRule> Create(
        Guid tenantId,
        string name,
        string triggerEvent,
        string? conditionJson,
        string actionType,
        string? actionPayloadJson,
        bool isEnabled = true,
        DateTimeOffset? utcNow = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<AutomationRule>(Error.Validation("AutomationRule.InvalidTenant", "Tenant ID is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<AutomationRule>(Error.Validation("AutomationRule.NameRequired", "Rule name is required."));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            return Result.Failure<AutomationRule>(Error.Validation(
                "AutomationRule.NameTooLong",
                $"Rule name cannot exceed {MaxNameLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(triggerEvent))
        {
            return Result.Failure<AutomationRule>(Error.Validation("AutomationRule.TriggerRequired", "Trigger event is required."));
        }

        var normalizedTrigger = SupportedTriggerEvents.FirstOrDefault(
            t => string.Equals(t, triggerEvent.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedTrigger == null)
        {
            return Result.Failure<AutomationRule>(Error.Validation(
                "AutomationRule.UnsupportedTrigger",
                $"TriggerEvent must be one of: {string.Join(", ", SupportedTriggerEvents)}."));
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            return Result.Failure<AutomationRule>(Error.Validation("AutomationRule.ActionTypeRequired", "Action type is required."));
        }

        var normalizedAction = SupportedActionTypes.FirstOrDefault(
            a => string.Equals(a, actionType.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedAction == null)
        {
            return Result.Failure<AutomationRule>(Error.Validation(
                "AutomationRule.UnsupportedAction",
                $"ActionType must be one of: {string.Join(", ", SupportedActionTypes)}."));
        }

        var normalizedCondition = string.IsNullOrWhiteSpace(conditionJson) ? "{}" : conditionJson.Trim();
        if (!IsValidJsonObject(normalizedCondition))
        {
            return Result.Failure<AutomationRule>(Error.Validation(
                "AutomationRule.InvalidConditionJson",
                "ConditionJson must be a valid JSON object."));
        }

        var normalizedPayload = string.IsNullOrWhiteSpace(actionPayloadJson) ? "{}" : actionPayloadJson.Trim();
        if (!IsValidJsonObject(normalizedPayload))
        {
            return Result.Failure<AutomationRule>(Error.Validation(
                "AutomationRule.InvalidActionPayloadJson",
                "ActionPayloadJson must be a valid JSON object."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        return Result.Success(new AutomationRule(
            Guid.NewGuid(),
            tenantId,
            trimmedName,
            normalizedTrigger,
            normalizedCondition,
            normalizedAction,
            normalizedPayload,
            isEnabled,
            now));
    }

    public void SetEnabled(bool isEnabled, DateTimeOffset? utcNow = null)
    {
        IsEnabled = isEnabled;
        UpdatedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
    }

    public bool Toggle(DateTimeOffset? utcNow = null)
    {
        IsEnabled = !IsEnabled;
        UpdatedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
        return IsEnabled;
    }

    private static bool IsValidJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
