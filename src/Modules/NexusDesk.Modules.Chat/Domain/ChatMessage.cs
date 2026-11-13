using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Chat.Domain;

public sealed class ChatMessage : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid ChatSessionId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string SenderRole { get; private set; }
    public string Content { get; private set; }
    public DateTimeOffset SentAtUtc { get; private set; }

    public ChatMessage(
        Guid id,
        Guid tenantId,
        Guid chatSessionId,
        Guid senderUserId,
        string content) : base(id)
    {
        TenantId = tenantId;
        ChatSessionId = chatSessionId;
        SenderUserId = senderUserId;
        SenderRole = "Customer";
        Content = content ?? string.Empty;
        SentAtUtc = DateTimeOffset.UtcNow;
    }

    public ChatMessage(
        Guid id,
        Guid tenantId,
        Guid chatSessionId,
        Guid senderUserId,
        string senderRole,
        string content,
        DateTimeOffset sentAtUtc) : base(id)
    {
        TenantId = tenantId;
        ChatSessionId = chatSessionId;
        SenderUserId = senderUserId;
        SenderRole = senderRole;
        Content = content;
        SentAtUtc = sentAtUtc;
    }

    public static Result<ChatMessage> Create(
        Guid tenantId,
        Guid chatSessionId,
        Guid senderUserId,
        string senderRole,
        string content,
        DateTimeOffset? utcNow = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<ChatMessage>(Error.Validation("ChatMessage.InvalidTenant", "TenantId is required."));
        }

        if (chatSessionId == Guid.Empty)
        {
            return Result.Failure<ChatMessage>(Error.Validation("ChatMessage.InvalidSession", "ChatSessionId is required."));
        }

        if (senderUserId == Guid.Empty)
        {
            return Result.Failure<ChatMessage>(Error.Validation("ChatMessage.InvalidSender", "SenderUserId is required."));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure<ChatMessage>(Error.Validation("ChatMessage.EmptyContent", "Message content cannot be empty."));
        }

        var normalizedRole = string.Equals(senderRole, "Agent", StringComparison.OrdinalIgnoreCase)
            ? "Agent"
            : "Customer";

        return Result.Success(new ChatMessage(
            Guid.NewGuid(),
            tenantId,
            chatSessionId,
            senderUserId,
            normalizedRole,
            content.Trim(),
            utcNow ?? DateTimeOffset.UtcNow));
    }

    private ChatMessage() : base()
    {
        SenderRole = string.Empty;
        Content = string.Empty;
    }
}
