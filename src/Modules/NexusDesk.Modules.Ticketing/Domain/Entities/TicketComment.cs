using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain.Entities;

public sealed class TicketComment : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public bool IsCustomer { get; private set; }
    public CommentType Type { get; private set; }
    public string Body { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal TicketComment(
        Guid id,
        Guid tenantId,
        Guid ticketId,
        Guid authorUserId,
        bool isCustomer,
        CommentType type,
        string body,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        TicketId = ticketId;
        AuthorUserId = authorUserId;
        IsCustomer = isCustomer;
        Type = type;
        Body = body;
        CreatedAtUtc = createdAtUtc;
    }

    private TicketComment() : base()
    {
        Body = string.Empty;
    }
}
