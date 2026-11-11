using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain.Entities;

public sealed class TicketTag : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid TicketId { get; private set; }
    public string Name { get; private set; }

    internal TicketTag(
        Guid id,
        Guid tenantId,
        Guid ticketId,
        string name) : base(id)
    {
        TenantId = tenantId;
        TicketId = ticketId;
        Name = NormalizeSlug(name);
    }

    private TicketTag() : base()
    {
        Name = string.Empty;
    }

    public static string NormalizeSlug(string name)
    {
        return name.Trim().ToLowerInvariant();
    }
}
