using System.Data.Common;

namespace NexusDesk.SharedKernel.Outbox;

/// <summary>
/// Represents an integration event to be published reliably via the Transactional Outbox pattern.
/// </summary>
public sealed record OutboxIntegrationEvent(
    Guid TenantId,
    string EventType,
    string PayloadJson,
    Guid? EventId = null,
    DateTimeOffset? OccurredAtUtc = null);

/// <summary>
/// Integration contract for atomically persisting outbox messages.
/// </summary>
public interface IOutboxEventEnqueuer
{
    /// <summary>
    /// Enqueues an integration event using the module's ambient DbContext.
    /// </summary>
    Task EnqueueAsync(
        OutboxIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues an integration event directly using an existing database connection and transaction,
    /// guaranteeing atomicity with the caller's database changes.
    /// </summary>
    Task EnqueueTransactionalAsync(
        DbConnection connection,
        DbTransaction? transaction,
        OutboxIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default);
}
