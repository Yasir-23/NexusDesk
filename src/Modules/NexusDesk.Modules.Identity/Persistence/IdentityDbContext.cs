using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Security;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Identity.Persistence;

public class IdentityDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IPiiEncryptionService _piiEncryptionService;

    public Guid CurrentTenantId => _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRoleAssignment> UserRoles => Set<UserRoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OAuthAuthorizationCode> AuthorizationCodes => Set<OAuthAuthorizationCode>();

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        ITenantContextAccessor tenantContextAccessor,
        IPiiEncryptionService piiEncryptionService) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _piiEncryptionService = piiEncryptionService;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var piiConverter = new ValueConverter<string?, string?>(
            v => _piiEncryptionService.Encrypt(v),
            v => _piiEncryptionService.Decrypt(v));

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users", "identity");
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).HasColumnName("id");
            b.Property(u => u.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(u => u.TenantId);
            b.Property(u => u.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
            b.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256).IsRequired();
            b.Property(u => u.PasswordHash).HasColumnName("password_hash").HasColumnType("text").IsRequired();
            b.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
            b.Property(u => u.PhoneNumber).HasColumnName("encrypted_phone_number").HasColumnType("text").HasConversion(piiConverter);
            b.Property(u => u.SsoProvider).HasColumnName("sso_provider").HasMaxLength(50);
            b.Property(u => u.SsoSubjectId).HasColumnName("sso_subject_id").HasMaxLength(200);
            b.Property(u => u.Role).HasColumnName("role").HasMaxLength(30).IsRequired();
            b.Property(u => u.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(u => u.IsAnonymized).HasColumnName("is_anonymized").IsRequired();
            b.Property(u => u.LastLoginAtUtc).HasColumnName("last_login_at_utc");
            b.Property(u => u.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(u => u.UpdatedAtUtc).HasColumnName("updated_at_utc");

            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
            b.HasQueryFilter(e => CurrentTenantId == Guid.Empty || e.TenantId == CurrentTenantId || e.TenantId == Guid.Empty);
        });

        modelBuilder.Entity<UserRoleAssignment>(b =>
        {
            b.ToTable("user_role_assignments", "identity");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
            b.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(r => r.TenantId);
            b.HasIndex(r => new { r.UserId, r.Role }).IsUnique();
            b.Property(r => r.Role).HasColumnName("role").HasMaxLength(30).IsRequired();
            b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasQueryFilter(e => CurrentTenantId == Guid.Empty || e.TenantId == CurrentTenantId || e.TenantId == Guid.Empty);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens", "identity");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).HasColumnName("id");
            b.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
            b.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(t => t.TenantId);
            b.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.Property(t => t.JwtId).HasColumnName("jwt_id").HasMaxLength(64).IsRequired();
            b.Property(t => t.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
            b.Property(t => t.RevokedAtUtc).HasColumnName("revoked_at_utc");
            b.Property(t => t.ReplacedByTokenHash).HasColumnName("replaced_by_token_hash").HasMaxLength(128);
            b.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasQueryFilter(e => CurrentTenantId == Guid.Empty || e.TenantId == CurrentTenantId || e.TenantId == Guid.Empty);
        });

        modelBuilder.Entity<OAuthAuthorizationCode>(b =>
        {
            b.ToTable("oauth_authorization_codes", "identity");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasColumnName("id");
            b.Property(c => c.CodeHash).HasColumnName("code_hash").HasMaxLength(128).IsRequired();
            b.HasIndex(c => c.CodeHash).IsUnique();
            b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
            b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(c => c.TenantId);
            b.Property(c => c.ClientId).HasColumnName("client_id").HasMaxLength(100).IsRequired();
            b.Property(c => c.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(500).IsRequired();
            b.Property(c => c.CodeChallenge).HasColumnName("code_challenge").HasMaxLength(200).IsRequired();
            b.Property(c => c.CodeChallengeMethod).HasColumnName("code_challenge_method").HasMaxLength(20).IsRequired();
            b.Property(c => c.Scope).HasColumnName("scope").HasMaxLength(200);
            b.Property(c => c.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
            b.Property(c => c.ConsumedAtUtc).HasColumnName("consumed_at_utc");
            b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasQueryFilter(e => CurrentTenantId == Guid.Empty || e.TenantId == CurrentTenantId || e.TenantId == Guid.Empty);
        });
    }
}
