using NexusDesk.Modules.Chat.Domain;

namespace NexusDesk.Modules.Chat.Contracts;

public static class ChatMappingExtensions
{
    public static ChatMessageDto ToDto(this ChatMessage message)
    {
        return new ChatMessageDto(
            message.Id,
            message.TenantId,
            message.ChatSessionId,
            message.SenderUserId,
            message.SenderRole,
            message.Content,
            message.SentAtUtc);
    }

    public static ChatSessionDto ToDto(this ChatSession session)
    {
        var messages = session.Messages
            .OrderBy(m => m.SentAtUtc)
            .Select(m => m.ToDto())
            .ToList();

        return new ChatSessionDto(
            session.Id,
            session.TenantId,
            session.CustomerId,
            session.AssignedAgentId,
            session.Subject,
            session.Status,
            session.ConvertedTicketId,
            session.CreatedAtUtc,
            session.ClosedAtUtc,
            messages);
    }

    public static ChatSessionSummaryDto ToSummaryDto(this ChatSession session)
    {
        return new ChatSessionSummaryDto(
            session.Id,
            session.TenantId,
            session.CustomerId,
            session.AssignedAgentId,
            session.Subject,
            session.Status,
            session.ConvertedTicketId,
            session.CreatedAtUtc,
            session.ClosedAtUtc,
            session.Messages.Count);
    }
}
