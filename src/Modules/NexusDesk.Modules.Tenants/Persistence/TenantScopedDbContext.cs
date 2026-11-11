using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Domain;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Persistence;

public class TenantScopedDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public Guid CurrentTenantId => _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<TenantSetting> Settings => Set<TenantSetting>();

    public TenantScopedDbContext(
        DbContextOptions<TenantScopedDbContext> options,
        ITenantContextAccessor tenantContextAccessor) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenantMembership>(b =>
        {
            b.ToTable("tenant_memberships", "tenants");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id");
            b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(m => m.TenantId);
            b.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
            b.Property(m => m.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
            b.Property(m => m.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
            b.Property(m => m.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
            b.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TenantSetting>(b =>
        {
            b.ToTable("tenant_settings", "tenants");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasColumnName("id");
            b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(s => s.TenantId);
            b.Property(s => s.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
            b.Property(s => s.Value).HasColumnName("value").HasColumnType("text").IsRequired();
            b.Property(s => s.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(s => s.UpdatedAtUtc).HasColumnName("updated_at_utc");
            b.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });
    }
}
