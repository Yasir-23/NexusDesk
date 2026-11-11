using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Identity.Domain;

public sealed class RefreshToken : Entity<Guid>, ITenantScoped
{
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string TokenHash { get; private set; }
    public string JwtId { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    public RefreshToken(
        Guid id,
        Guid userId,
        Guid tenantId,
        string tokenHash,
        string jwtId,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        TokenHash = tokenHash ?? throw new ArgumentNullException(nameof(tokenHash));
        JwtId = jwtId ?? throw new ArgumentNullException(nameof(jwtId));
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    private RefreshToken() : base()
    {
        TokenHash = string.Empty;
        JwtId = string.Empty;
    }

    public void Revoke(string? replacedByTokenHash, DateTimeOffset now)
    {
        RevokedAtUtc = now;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
