using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Domain;

public sealed class TenantMembership : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Role { get; private set; }

    public TenantMembership(Guid id, Guid tenantId, Guid userId, string role) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role ?? throw new ArgumentNullException(nameof(role));
    }

    private TenantMembership() : base()
    {
        Role = string.Empty;
    }
}
