using NexusDesk.Modules.Chat.Domain;

namespace NexusDesk.Modules.Chat.Contracts;

public sealed record CreateChatSessionRequest(string Subject);

public sealed record SendChatMessageRequest(
    string? Content = null,
    string? Message = null)
{
    public string MessageText => Content ?? Message ?? string.Empty;
}

public sealed record CloseChatSessionRequest(bool ConvertToTicket = false);

public sealed record ChatMessageDto(
    Guid Id,
    Guid TenantId,
    Guid ChatSessionId,
    Guid SenderUserId,
    string SenderRole,
    string Content,
    DateTimeOffset SentAtUtc);

public sealed record ChatSessionDto(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    Guid? AssignedAgentId,
    string Subject,
    ChatSessionStatus Status,
    Guid? ConvertedTicketId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<ChatMessageDto>? Messages = null);

public sealed record ChatSessionSummaryDto(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    Guid? AssignedAgentId,
    string Subject,
    ChatSessionStatus Status,
    Guid? ConvertedTicketId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    int MessageCount = 0);

public sealed record ChatSessionClosedDto(
    Guid Id,
    Guid TenantId,
    ChatSessionStatus Status,
    Guid? ConvertedTicketId,
    DateTimeOffset ClosedAtUtc);

public sealed record ChatTypingDto(
    Guid SessionId,
    Guid UserId,
    string? UserName,
    bool IsTyping);
