using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain.Events;

public sealed record TicketCreatedDomainEvent(
    Guid TicketId,
    Guid TenantId,
    string TicketNumber) : DomainEvent;

public sealed record TicketAssignedDomainEvent(
    Guid TicketId,
    Guid? AgentId,
    Guid PerformedBy) : DomainEvent;

public sealed record TicketStatusChangedDomainEvent(
    Guid TicketId,
    TicketStatus OldStatus,
    TicketStatus NewStatus,
    Guid PerformedBy) : DomainEvent;

public sealed record TicketCommentAddedDomainEvent(
    Guid TicketId,
    Guid CommentId,
    CommentType Type,
    bool IsCustomer) : DomainEvent;
