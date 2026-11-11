using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Identity.Domain;

public sealed class User : AggregateRoot<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Email { get; private set; }
    public string NormalizedEmail { get; private set; }
    public string PasswordHash { get; private set; }
    public string FullName { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? SsoProvider { get; private set; }
    public string? SsoSubjectId { get; private set; }
    public string Role { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsAnonymized { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public User(
        Guid id,
        Guid tenantId,
        string email,
        string fullName,
        string role,
        string passwordHash,
        string? phoneNumber = null,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        Email = email ?? throw new ArgumentNullException(nameof(email));
        NormalizedEmail = email.Trim().ToUpperInvariant();
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        Role = role ?? throw new ArgumentNullException(nameof(role));
        PasswordHash = passwordHash ?? string.Empty;
        PhoneNumber = phoneNumber;
        IsActive = true;
        IsAnonymized = false;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    public User(Guid id, Guid tenantId, string email, string fullName, string role)
        : this(id, tenantId, email, fullName, role, string.Empty, null, null)
    {
    }

    private User() : base()
    {
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        PasswordHash = string.Empty;
        FullName = string.Empty;
        Role = string.Empty;
    }

    public void SetPassword(string newPasswordHash, DateTimeOffset now)
    {
        PasswordHash = newPasswordHash ?? throw new ArgumentNullException(nameof(newPasswordHash));
        UpdatedAtUtc = now;
    }

    public void UpdateProfile(string fullName, string? phoneNumber, DateTimeOffset now)
    {
        FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
        PhoneNumber = phoneNumber;
        UpdatedAtUtc = now;
    }

    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void LinkSso(string provider, string subjectId, DateTimeOffset now)
    {
        SsoProvider = provider;
        SsoSubjectId = subjectId;
        UpdatedAtUtc = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAtUtc = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAtUtc = now;
    }

    public void Anonymize(DateTimeOffset now)
    {
        Email = $"anonymized-{Id:N}@nexusdesk.invalid";
        NormalizedEmail = Email.ToUpperInvariant();
        FullName = "GDPR Redacted";
        PhoneNumber = null;
        PasswordHash = string.Empty;
        SsoProvider = null;
        SsoSubjectId = null;
        IsAnonymized = true;
        UpdatedAtUtc = now;
    }
}
