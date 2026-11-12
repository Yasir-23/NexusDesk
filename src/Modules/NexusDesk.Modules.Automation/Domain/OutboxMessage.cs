using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Domain;

public enum OutboxMessageStatus
{
    Pending,
    Processed,
    DeadLettered
}

public sealed class OutboxMessage : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string EventType { get; private set; }
    public string PayloadJson { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset NextRetryAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public OutboxMessage(
        Guid id,
        Guid tenantId,
        string eventType,
        string payloadJson,
        OutboxMessageStatus status,
        int retryCount,
        DateTimeOffset nextRetryAtUtc,
        string? lastError,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? processedAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        EventType = eventType;
        PayloadJson = payloadJson;
        Status = status;
        RetryCount = retryCount;
        NextRetryAtUtc = nextRetryAtUtc;
        LastError = lastError;
        CreatedAtUtc = createdAtUtc;
        ProcessedAtUtc = processedAtUtc;
    }

    private OutboxMessage() : base()
    {
        EventType = string.Empty;
        PayloadJson = "{}";
        Status = OutboxMessageStatus.Pending;
    }

    public static OutboxMessage CreatePending(
        Guid id,
        Guid tenantId,
        string eventType,
        string payloadJson,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;
        return new OutboxMessage(
            id == Guid.Empty ? Guid.NewGuid() : id,
            tenantId,
            eventType,
            payloadJson,
            OutboxMessageStatus.Pending,
            retryCount: 0,
            nextRetryAtUtc: now,
            lastError: null,
            createdAtUtc: now,
            processedAtUtc: null);
    }

    public void MarkProcessed(DateTimeOffset? utcNow = null)
    {
        Status = OutboxMessageStatus.Processed;
        ProcessedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
        LastError = null;
    }

    public void RecordRetry(string error, DateTimeOffset nextRetryAtUtc)
    {
        RetryCount++;
        LastError = error;
        NextRetryAtUtc = nextRetryAtUtc;
        Status = OutboxMessageStatus.Pending;
    }

    public void ScheduleNextRetryAt(DateTimeOffset nextRetryAtUtc)
    {
        NextRetryAtUtc = nextRetryAtUtc;
    }

    public void MarkDeadLettered(string error, DateTimeOffset? utcNow = null)
    {
        RetryCount++;
        LastError = error;
        Status = OutboxMessageStatus.DeadLettered;
        ProcessedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
    }
}
