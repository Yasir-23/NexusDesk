using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain.Entities;

public sealed class TicketActivityLog : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid PerformedByUserId { get; private set; }
    public string Action { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    internal TicketActivityLog(
        Guid id,
        Guid tenantId,
        Guid ticketId,
        Guid performedByUserId,
        string action,
        string? oldValue,
        string? newValue,
        DateTimeOffset timestampUtc) : base(id)
    {
        TenantId = tenantId;
        TicketId = ticketId;
        PerformedByUserId = performedByUserId;
        Action = action;
        OldValue = oldValue;
        NewValue = newValue;
        TimestampUtc = timestampUtc;
    }

    private TicketActivityLog() : base()
    {
        Action = string.Empty;
    }
}
