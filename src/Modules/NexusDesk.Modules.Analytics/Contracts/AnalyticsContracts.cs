namespace NexusDesk.Modules.Analytics.Contracts;

public sealed record CreateAuditLogRequest(
    string Action,
    string ResourceType,
    string ResourceId,
    string? MetadataJson = null,
    string? IpAddress = null);

public sealed record CreateNotificationRequest(
    Guid RecipientUserId,
    string Type,
    string Title,
    string Message,
    string? LinkUrl = null);

public sealed record AuditLogEntryResponse(
    Guid Id,
    Guid TenantId,
    Guid? ActorUserId,
    string? ActorEmail,
    string Action,
    string ResourceType,
    string ResourceId,
    string? MetadataJson,
    string? IpAddress,
    DateTimeOffset CreatedAtUtc);

public sealed record PagedAuditLogsResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record UserNotificationResponse(
    Guid Id,
    Guid TenantId,
    Guid RecipientUserId,
    string Type,
    string Title,
    string Message,
    string? LinkUrl,
    bool IsRead,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record PagedNotificationsResponse(
    IReadOnlyList<UserNotificationResponse> Items,
    int UnreadCount,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record MarkAllNotificationsReadResponse(int MarkedCount);
