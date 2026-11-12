using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.Outbox;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Outbox;

public sealed class TicketingOutboxIntegrationEventBridge :
    IDomainEventHandler<TicketCreatedDomainEvent>,
    IDomainEventHandler<TicketSlaBreachedDomainEvent>,
    IDomainEventHandler<TicketResolvedDomainEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TicketingDbContext _ticketingDb;
    private readonly IOutboxEventEnqueuer? _outboxEnqueuer;

    public TicketingOutboxIntegrationEventBridge(
        TicketingDbContext ticketingDb,
        IOutboxEventEnqueuer? outboxEnqueuer = null)
    {
        _ticketingDb = ticketingDb;
        _outboxEnqueuer = outboxEnqueuer;
    }

    public async Task HandleAsync(TicketCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_outboxEnqueuer == null)
        {
            return;
        }

        var title = domainEvent.Title;
        var priority = domainEvent.Priority;
        var customerId = domainEvent.CustomerId;
        var createdAtUtc = domainEvent.CreatedAtUtc ?? DateTimeOffset.UtcNow;

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(priority) || !customerId.HasValue)
        {
            var ticket = await _ticketingDb.Tickets
                .AsNoTracking()
                .Where(t => t.Id == domainEvent.TicketId && t.TenantId == domainEvent.TenantId)
                .Select(t => new { t.Title, t.Priority, t.CustomerId, t.CreatedAtUtc })
                .FirstOrDefaultAsync(cancellationToken);

            if (ticket != null)
            {
                title ??= ticket.Title;
                priority ??= ticket.Priority.ToString();
                customerId ??= ticket.CustomerId;
                createdAtUtc = ticket.CreatedAtUtc;
            }
        }

        var eventId = Guid.NewGuid();
        var envelope = new
        {
            eventId,
            deliveryId = eventId,
            tenantId = domainEvent.TenantId,
            eventType = "ticket.created",
            occurredAtUtc = createdAtUtc,
            timestampUtc = createdAtUtc,
            data = new
            {
                ticketId = domainEvent.TicketId,
                ticketNumber = domainEvent.TicketNumber,
                title = title ?? string.Empty,
                priority = priority ?? "Medium",
                customerId = customerId ?? Guid.Empty
            }
        };

        var payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var integrationEvent = new OutboxIntegrationEvent(
            domainEvent.TenantId,
            "ticket.created",
            payloadJson,
            eventId,
            createdAtUtc);

        await EnqueueOutboxEventAsync(integrationEvent, cancellationToken);
    }

    public async Task HandleAsync(TicketSlaBreachedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_outboxEnqueuer == null)
        {
            return;
        }

        var eventId = Guid.NewGuid();
        var occurredAtUtc = domainEvent.BreachedAtUtc;
        var envelope = new
        {
            eventId,
            deliveryId = eventId,
            tenantId = domainEvent.TenantId,
            eventType = "ticket.sla_breached",
            occurredAtUtc,
            timestampUtc = occurredAtUtc,
            data = new
            {
                ticketId = domainEvent.TicketId,
                ticketNumber = domainEvent.TicketNumber,
                priority = domainEvent.Priority,
                breachType = domainEvent.BreachType
            }
        };

        var payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var integrationEvent = new OutboxIntegrationEvent(
            domainEvent.TenantId,
            "ticket.sla_breached",
            payloadJson,
            eventId,
            occurredAtUtc);

        await EnqueueOutboxEventAsync(integrationEvent, cancellationToken);
    }

    public async Task HandleAsync(TicketResolvedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_outboxEnqueuer == null)
        {
            return;
        }

        var eventId = Guid.NewGuid();
        var occurredAtUtc = domainEvent.ResolvedAtUtc;
        var envelope = new
        {
            eventId,
            deliveryId = eventId,
            tenantId = domainEvent.TenantId,
            eventType = "ticket.resolved",
            occurredAtUtc,
            timestampUtc = occurredAtUtc,
            data = new
            {
                ticketId = domainEvent.TicketId,
                ticketNumber = domainEvent.TicketNumber,
                resolvedByAgentId = domainEvent.ResolvedByAgentId,
                resolutionSummary = domainEvent.ResolutionSummary
            }
        };

        var payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var integrationEvent = new OutboxIntegrationEvent(
            domainEvent.TenantId,
            "ticket.resolved",
            payloadJson,
            eventId,
            occurredAtUtc);

        await EnqueueOutboxEventAsync(integrationEvent, cancellationToken);
    }

    private async Task EnqueueOutboxEventAsync(
        OutboxIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var currentTx = _ticketingDb.Database.CurrentTransaction?.GetDbTransaction();
        var conn = _ticketingDb.Database.GetDbConnection();

        if (currentTx != null)
        {
            await _outboxEnqueuer!.EnqueueTransactionalAsync(conn, currentTx, integrationEvent, cancellationToken);
        }
        else
        {
            await _outboxEnqueuer!.EnqueueAsync(integrationEvent, cancellationToken);
        }
    }
}
