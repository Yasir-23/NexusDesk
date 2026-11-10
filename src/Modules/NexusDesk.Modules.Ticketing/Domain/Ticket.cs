using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain;

public sealed class Ticket : AggregateRoot<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string TicketNumber { get; private set; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public string Priority { get; private set; }
    public string Status { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }

    public Ticket(
        Guid id,
        Guid tenantId,
        string ticketNumber,
        string title,
        string description,
        string priority,
        Guid customerId) : base(id)
    {
        TenantId = tenantId;
        TicketNumber = ticketNumber ?? throw new ArgumentNullException(nameof(ticketNumber));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Priority = priority ?? throw new ArgumentNullException(nameof(priority));
        CustomerId = customerId;
        Status = "New";
    }

    private Ticket() : base()
    {
        TicketNumber = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Priority = string.Empty;
        Status = string.Empty;
    }
}
