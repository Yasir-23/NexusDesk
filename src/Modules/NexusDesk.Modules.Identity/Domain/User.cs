using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Identity.Domain;

public sealed class User : AggregateRoot<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Email { get; private set; }
    public string FullName { get; private set; }
    public string Role { get; private set; }
    public bool IsActive { get; private set; }

    public User(Guid id, Guid tenantId, string email, string fullName, string role) : base(id)
    {
        TenantId = tenantId;
        Email = email ?? throw new ArgumentNullException(nameof(email));
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        Role = role ?? throw new ArgumentNullException(nameof(role));
        IsActive = true;
    }

    private User() : base()
    {
        Email = string.Empty;
        FullName = string.Empty;
        Role = string.Empty;
    }
}
