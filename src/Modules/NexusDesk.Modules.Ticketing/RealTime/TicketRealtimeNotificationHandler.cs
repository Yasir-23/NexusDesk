using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.RealTime;

public sealed class TicketRealtimeNotificationHandler :
    IDomainEventHandler<TicketCreatedDomainEvent>,
    IDomainEventHandler<TicketAssignedDomainEvent>,
    IDomainEventHandler<TicketStatusChangedDomainEvent>,
    IDomainEventHandler<TicketPriorityChangedDomainEvent>,
    IDomainEventHandler<TicketCommentAddedDomainEvent>
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly IHubContext<TicketHub, ITicketHubClient> _hubContext;

    public TicketRealtimeNotificationHandler(
        TicketingDbContext ticketingDb,
        IHubContext<TicketHub, ITicketHubClient> hubContext)
    {
        _ticketingDb = ticketingDb;
        _hubContext = hubContext;
    }

    public async Task HandleAsync(TicketCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var notification = await BuildTicketNotificationAsync(
            domainEvent.TicketId,
            "TicketCreated",
            domainEvent.OccurredOnUtc,
            cancellationToken);

        if (notification == null)
        {
            return;
        }

        IReadOnlyList<string> groups =
        [
            TicketHubGroups.TenantStaff(notification.TenantId),
            TicketHubGroups.TicketRoom(notification.TenantId, notification.TicketId),
            TicketHubGroups.TenantUser(notification.TenantId, notification.CustomerId)
        ];

        await _hubContext.Clients.Groups(groups).TicketCreated(notification);
    }

    public Task HandleAsync(TicketAssignedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => BroadcastTicketUpdatedAsync(
            domainEvent.TicketId,
            "TicketAssigned",
            domainEvent.OccurredOnUtc,
            includeCustomerUserGroup: true,
            cancellationToken);

    public Task HandleAsync(TicketStatusChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => BroadcastTicketUpdatedAsync(
            domainEvent.TicketId,
            "TicketStatusChanged",
            domainEvent.OccurredOnUtc,
            includeCustomerUserGroup: true,
            cancellationToken);

    public Task HandleAsync(TicketPriorityChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => BroadcastTicketUpdatedAsync(
            domainEvent.TicketId,
            "TicketPriorityChanged",
            domainEvent.OccurredOnUtc,
            includeCustomerUserGroup: false,
            cancellationToken);

    private async Task BroadcastTicketUpdatedAsync(
        Guid ticketId,
        string eventType,
        DateTimeOffset occurredAtUtc,
        bool includeCustomerUserGroup,
        CancellationToken cancellationToken)
    {
        var notification = await BuildTicketNotificationAsync(
            ticketId,
            eventType,
            occurredAtUtc,
            cancellationToken);

        if (notification == null)
        {
            return;
        }

        IReadOnlyList<string> groups = includeCustomerUserGroup
            ? [
                TicketHubGroups.TenantStaff(notification.TenantId),
                TicketHubGroups.TicketRoom(notification.TenantId, notification.TicketId),
                TicketHubGroups.TenantUser(notification.TenantId, notification.CustomerId)
              ]
            : [
                TicketHubGroups.TenantStaff(notification.TenantId),
                TicketHubGroups.TicketRoom(notification.TenantId, notification.TicketId)
              ];

        await _hubContext.Clients.Groups(groups).TicketUpdated(notification);
    }

    public async Task HandleAsync(TicketCommentAddedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var comment = await _ticketingDb.TicketComments
            .AsNoTracking()
            .Where(c => c.Id == domainEvent.CommentId && c.TicketId == domainEvent.TicketId)
            .Select(c => new TicketCommentRealtimeNotification(
                c.TicketId,
                c.TenantId,
                c.Id,
                c.AuthorUserId,
                c.IsCustomer,
                c.Type,
                c.Body,
                c.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        if (comment == null)
        {
            return;
        }

        IReadOnlyList<string> groups = comment.Type == CommentType.InternalNote
            ? [
                TicketHubGroups.TicketRoomStaff(comment.TenantId, comment.TicketId),
                TicketHubGroups.TenantStaff(comment.TenantId)
              ]
            : [
                TicketHubGroups.TicketRoom(comment.TenantId, comment.TicketId),
                TicketHubGroups.TenantStaff(comment.TenantId)
              ];

        await _hubContext.Clients.Groups(groups).TicketCommentAdded(comment);
    }

    private async Task<TicketRealtimeNotification?> BuildTicketNotificationAsync(
        Guid ticketId,
        string eventType,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        return await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.Id == ticketId)
            .Select(t => new TicketRealtimeNotification(
                t.Id,
                t.TenantId,
                t.TicketNumber,
                t.Title,
                t.Status,
                t.Priority,
                t.CustomerId,
                t.AssignedAgentId,
                eventType,
                occurredAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
