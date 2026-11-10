using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Chat.Domain;

public sealed class ChatMessage : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Message { get; private set; }
    public DateTimeOffset SentAtUtc { get; private set; }

    public ChatMessage(Guid id, Guid tenantId, Guid ticketId, Guid senderId, string message) : base(id)
    {
        TenantId = tenantId;
        TicketId = ticketId;
        SenderId = senderId;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        SentAtUtc = DateTimeOffset.UtcNow;
    }

    private ChatMessage() : base()
    {
        Message = string.Empty;
    }
}
