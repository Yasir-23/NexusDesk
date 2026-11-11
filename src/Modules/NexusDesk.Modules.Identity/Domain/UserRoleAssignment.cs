using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Identity.Domain;

public sealed class UserRoleAssignment : Entity<Guid>, ITenantScoped
{
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Role { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public UserRoleAssignment(
        Guid id,
        Guid userId,
        Guid tenantId,
        string role,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        Role = role ?? throw new ArgumentNullException(nameof(role));
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    private UserRoleAssignment() : base()
    {
        Role = string.Empty;
    }
}
