using NexusDesk.Modules.Ticketing.Domain.Enums;

namespace NexusDesk.Modules.Ticketing.RealTime;

public sealed record TicketRealtimeNotification(
    Guid TicketId,
    Guid TenantId,
    string TicketNumber,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    Guid CustomerId,
    Guid? AssignedAgentId,
    string EventType,
    DateTimeOffset OccurredAtUtc);

public sealed record TicketCommentRealtimeNotification(
    Guid TicketId,
    Guid TenantId,
    Guid CommentId,
    Guid AuthorUserId,
    bool IsCustomer,
    CommentType Type,
    string Body,
    DateTimeOffset CreatedAtUtc);

public sealed record TicketPresenceAgentDto(
    Guid UserId,
    string DisplayName,
    bool IsTyping,
    DateTimeOffset LastActivityUtc);

public sealed record TicketPresenceNotification(
    Guid TicketId,
    Guid TenantId,
    IReadOnlyList<TicketPresenceAgentDto> ActiveAgents,
    DateTimeOffset UpdatedAtUtc);

public sealed record TicketPresenceResponse(
    Guid TicketId,
    Guid TenantId,
    IReadOnlyList<TicketPresenceAgentDto> ActiveAgents,
    DateTimeOffset RetrievedAtUtc);
