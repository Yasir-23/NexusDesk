using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Identity.Domain;

public sealed class OAuthAuthorizationCode : Entity<Guid>, ITenantScoped
{
    public string CodeHash { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string ClientId { get; private set; }
    public string RedirectUri { get; private set; }
    public string CodeChallenge { get; private set; }
    public string CodeChallengeMethod { get; private set; }
    public string Scope { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;
    public bool IsConsumed => ConsumedAtUtc != null;
    public bool IsActive => !IsConsumed && !IsExpired;

    public OAuthAuthorizationCode(
        Guid id,
        string codeHash,
        Guid userId,
        Guid tenantId,
        string clientId,
        string redirectUri,
        string codeChallenge,
        string codeChallengeMethod,
        string scope,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        CodeHash = codeHash ?? throw new ArgumentNullException(nameof(codeHash));
        UserId = userId;
        TenantId = tenantId;
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        RedirectUri = redirectUri ?? throw new ArgumentNullException(nameof(redirectUri));
        CodeChallenge = codeChallenge ?? throw new ArgumentNullException(nameof(codeChallenge));
        CodeChallengeMethod = codeChallengeMethod ?? "S256";
        Scope = scope ?? string.Empty;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    private OAuthAuthorizationCode() : base()
    {
        CodeHash = string.Empty;
        ClientId = string.Empty;
        RedirectUri = string.Empty;
        CodeChallenge = string.Empty;
        CodeChallengeMethod = string.Empty;
        Scope = string.Empty;
    }

    public void Consume(DateTimeOffset now)
    {
        ConsumedAtUtc = now;
    }
}
