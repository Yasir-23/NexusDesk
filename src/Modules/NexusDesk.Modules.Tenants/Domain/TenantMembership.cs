using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Domain;

public sealed class TenantMembership : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Role { get; private set; }
    public string Email { get; private set; }
    public string FullName { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public TenantMembership(
        Guid id,
        Guid tenantId,
        Guid userId,
        string role,
        string email = "",
        string fullName = "",
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role ?? throw new ArgumentNullException(nameof(role));
        Email = email ?? string.Empty;
        FullName = fullName ?? string.Empty;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    private TenantMembership() : base()
    {
        Role = string.Empty;
        Email = string.Empty;
        FullName = string.Empty;
    }
}
