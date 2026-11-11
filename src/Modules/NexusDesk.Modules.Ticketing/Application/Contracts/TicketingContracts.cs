using NexusDesk.Modules.Ticketing.Domain.Enums;

namespace NexusDesk.Modules.Ticketing.Application.Contracts;

public sealed record CreateTicketRequest(
    string Title,
    string Description,
    TicketPriority Priority = TicketPriority.Medium,
    TicketChannel Channel = TicketChannel.WebPortal,
    Guid? CustomerId = null,
    IReadOnlyList<string>? Tags = null);

public sealed record AssignTicketRequest(
    Guid AgentId,
    uint? RowVersion = null);

public sealed record UpdateTicketStatusRequest(
    TicketStatus Status,
    string? ResolutionNotes = null,
    string? Reason = null,
    uint? RowVersion = null);

public sealed record UpdateTicketPriorityRequest(
    TicketPriority Priority,
    uint? RowVersion = null);

public sealed record AddCommentRequest(
    string Body,
    CommentType Type = CommentType.PublicReply);

public sealed record TicketSummaryResponse(
    Guid Id,
    Guid TenantId,
    string TicketNumber,
    string Title,
    TicketStatus Status,
    TicketPriority Priority,
    TicketChannel Channel,
    Guid CustomerId,
    Guid? AssignedAgentId,
    DateTimeOffset? FirstResponseDueUtc,
    DateTimeOffset? ResolutionDueUtc,
    DateTimeOffset? FirstRespondedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    uint RowVersion,
    IReadOnlyList<string> Tags);

public sealed record TicketCommentDto(
    Guid Id,
    Guid AuthorUserId,
    bool IsCustomer,
    CommentType Type,
    string Body,
    DateTimeOffset CreatedAtUtc);

public sealed record TicketActivityLogDto(
    Guid Id,
    Guid PerformedByUserId,
    string Action,
    string? OldValue,
    string? NewValue,
    DateTimeOffset TimestampUtc);

public sealed record TicketDetailsResponse(
    Guid Id,
    Guid TenantId,
    string TicketNumber,
    string Title,
    string Description,
    TicketStatus Status,
    TicketPriority Priority,
    TicketChannel Channel,
    Guid CustomerId,
    Guid? AssignedAgentId,
    string? ResolutionNotes,
    DateTimeOffset? FirstResponseDueUtc,
    DateTimeOffset? ResolutionDueUtc,
    DateTimeOffset? FirstRespondedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    uint RowVersion,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TicketCommentDto> Comments,
    IReadOnlyList<TicketActivityLogDto> ActivityLogs);

public sealed record PagedTicketsResponse(
    IReadOnlyList<TicketSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
