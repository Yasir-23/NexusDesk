using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Analytics.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Analytics.Persistence;

public class AnalyticsDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AnalyticsDbContext(
        DbContextOptions<AnalyticsDbContext> options,
        ITenantContextAccessor tenantContextAccessor) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("analytics");

        // 1. AuditLogEntry Entity Configuration
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("audit_log_entries");
            b.HasKey(a => a.Id);

            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
            b.Property(a => a.ActorEmail).HasColumnName("actor_email").HasMaxLength(256);
            b.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            b.Property(a => a.ResourceType).HasColumnName("resource_type").HasMaxLength(100).IsRequired();
            b.Property(a => a.ResourceId).HasColumnName("resource_id").HasMaxLength(100).IsRequired();
            b.Property(a => a.MetadataJson).HasColumnName("metadata_json").HasColumnType("text");
            b.Property(a => a.IpAddress).HasColumnName("ip_address").HasMaxLength(64);
            b.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasIndex(a => new { a.TenantId, a.CreatedAtUtc })
                .IsDescending(false, true)
                .HasDatabaseName("ix_audit_log_entries_tenant_created");

            b.HasIndex(a => new { a.TenantId, a.ResourceType, a.ResourceId })
                .HasDatabaseName("ix_audit_log_entries_tenant_resource");

            b.HasQueryFilter(a => a.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });

        // 2. UserNotification Entity Configuration
        modelBuilder.Entity<UserNotification>(b =>
        {
            b.ToTable("user_notifications");
            b.HasKey(n => n.Id);

            b.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(n => n.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(n => n.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
            b.Property(n => n.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
            b.Property(n => n.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            b.Property(n => n.Message).HasColumnName("message").HasMaxLength(1000).IsRequired();
            b.Property(n => n.LinkUrl).HasColumnName("link_url").HasMaxLength(500);
            b.Property(n => n.IsRead).HasColumnName("is_read").HasDefaultValue(false).IsRequired();
            b.Property(n => n.ReadAtUtc).HasColumnName("read_at_utc");
            b.Property(n => n.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.IsRead, n.CreatedAtUtc })
                .IsDescending(false, false, false, true)
                .HasDatabaseName("ix_user_notifications_tenant_user_read_created");

            b.HasQueryFilter(n => n.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });
    }
}
