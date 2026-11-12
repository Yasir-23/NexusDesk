using System.Data.Common;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.SharedKernel.Outbox;

namespace NexusDesk.Modules.Automation.Application;

public sealed class OutboxEventEnqueuer : IOutboxEventEnqueuer
{
    private readonly AutomationDbContext _automationDbContext;

    public OutboxEventEnqueuer(AutomationDbContext automationDbContext)
    {
        _automationDbContext = automationDbContext;
    }

    public async Task EnqueueAsync(
        OutboxIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var messageId = integrationEvent.EventId ?? Guid.NewGuid();
        var now = integrationEvent.OccurredAtUtc ?? DateTimeOffset.UtcNow;

        var message = OutboxMessage.CreatePending(
            messageId,
            integrationEvent.TenantId,
            integrationEvent.EventType,
            integrationEvent.PayloadJson,
            now);

        _automationDbContext.OutboxMessages.Add(message);
        await _automationDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task EnqueueTransactionalAsync(
        DbConnection connection,
        DbTransaction? transaction,
        OutboxIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var messageId = integrationEvent.EventId ?? Guid.NewGuid();
        var now = integrationEvent.OccurredAtUtc ?? DateTimeOffset.UtcNow;

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO automation.outbox_messages (
                id, tenant_id, event_type, payload_json, status, retry_count, next_retry_at_utc, last_error, created_at_utc, processed_at_utc
            )
            VALUES (
                @id, @tenant_id, @event_type, @payload_json::jsonb, @status, @retry_count, @next_retry_at_utc, @last_error, @created_at_utc, @processed_at_utc
            );
            """;

        var pId = cmd.CreateParameter();
        pId.ParameterName = "@id";
        pId.Value = messageId;
        cmd.Parameters.Add(pId);

        var pTenantId = cmd.CreateParameter();
        pTenantId.ParameterName = "@tenant_id";
        pTenantId.Value = integrationEvent.TenantId;
        cmd.Parameters.Add(pTenantId);

        var pEventType = cmd.CreateParameter();
        pEventType.ParameterName = "@event_type";
        pEventType.Value = integrationEvent.EventType;
        cmd.Parameters.Add(pEventType);

        var pPayload = cmd.CreateParameter();
        pPayload.ParameterName = "@payload_json";
        pPayload.Value = integrationEvent.PayloadJson;
        cmd.Parameters.Add(pPayload);

        var pStatus = cmd.CreateParameter();
        pStatus.ParameterName = "@status";
        pStatus.Value = nameof(OutboxMessageStatus.Pending);
        cmd.Parameters.Add(pStatus);

        var pRetryCount = cmd.CreateParameter();
        pRetryCount.ParameterName = "@retry_count";
        pRetryCount.Value = 0;
        cmd.Parameters.Add(pRetryCount);

        var pNextRetry = cmd.CreateParameter();
        pNextRetry.ParameterName = "@next_retry_at_utc";
        pNextRetry.Value = now;
        cmd.Parameters.Add(pNextRetry);

        var pLastError = cmd.CreateParameter();
        pLastError.ParameterName = "@last_error";
        pLastError.Value = DBNull.Value;
        cmd.Parameters.Add(pLastError);

        var pCreatedAt = cmd.CreateParameter();
        pCreatedAt.ParameterName = "@created_at_utc";
        pCreatedAt.Value = now;
        cmd.Parameters.Add(pCreatedAt);

        var pProcessedAt = cmd.CreateParameter();
        pProcessedAt.ParameterName = "@processed_at_utc";
        pProcessedAt.Value = DBNull.Value;
        cmd.Parameters.Add(pProcessedAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
