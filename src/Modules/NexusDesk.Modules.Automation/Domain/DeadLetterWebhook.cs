using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Domain;

public sealed class DeadLetterWebhook : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid OutboxMessageId { get; private set; }
    public Guid WebhookSubscriptionId { get; private set; }
    public string TargetUrl { get; private set; }
    public string EventType { get; private set; }
    public string PayloadJson { get; private set; }
    public int AttemptCount { get; private set; }
    public int? LastHttpStatusCode { get; private set; }
    public string LastError { get; private set; }
    public DateTimeOffset FailedAtUtc { get; private set; }

    public DeadLetterWebhook(
        Guid id,
        Guid tenantId,
        Guid outboxMessageId,
        Guid webhookSubscriptionId,
        string targetUrl,
        string eventType,
        string payloadJson,
        int attemptCount,
        int? lastHttpStatusCode,
        string lastError,
        DateTimeOffset failedAtUtc) : base(id)
    {
        TenantId = tenantId;
        OutboxMessageId = outboxMessageId;
        WebhookSubscriptionId = webhookSubscriptionId;
        TargetUrl = targetUrl;
        EventType = eventType;
        PayloadJson = payloadJson;
        AttemptCount = attemptCount;
        LastHttpStatusCode = lastHttpStatusCode;
        LastError = lastError;
        FailedAtUtc = failedAtUtc;
    }

    private DeadLetterWebhook() : base()
    {
        TargetUrl = string.Empty;
        EventType = string.Empty;
        PayloadJson = "{}";
        LastError = string.Empty;
    }

    public static DeadLetterWebhook Create(
        Guid tenantId,
        Guid outboxMessageId,
        Guid webhookSubscriptionId,
        string targetUrl,
        string eventType,
        string payloadJson,
        int attemptCount,
        int? lastHttpStatusCode,
        string lastError,
        DateTimeOffset? utcNow = null)
    {
        return new DeadLetterWebhook(
            Guid.NewGuid(),
            tenantId,
            outboxMessageId,
            webhookSubscriptionId,
            targetUrl,
            eventType,
            payloadJson,
            attemptCount,
            lastHttpStatusCode,
            lastError,
            utcNow ?? DateTimeOffset.UtcNow);
    }
}
