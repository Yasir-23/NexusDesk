using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Domain;

namespace NexusDesk.Modules.Tenants.Persistence;

public class CatalogDbContext : DbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantAuditLog> TenantAuditLogs => Set<TenantAuditLog>();

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants", "tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).HasColumnName("id");
            b.Property(t => t.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            b.Property(t => t.Subdomain).HasColumnName("subdomain").HasMaxLength(63).IsRequired();
            b.HasIndex(t => t.Subdomain).IsUnique();
            b.Property(t => t.PlanTier).HasColumnName("plan_tier").HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(t => t.IsolationStrategy).HasColumnName("isolation_strategy").HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(t => t.CustomConnectionString).HasColumnName("custom_connection_string").HasColumnType("text");
            b.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            b.Property(t => t.MaxAgents).HasColumnName("max_agents").IsRequired();
            b.Property(t => t.MaxTicketsPerMonth).HasColumnName("max_tickets_per_month").IsRequired();
            b.Property(t => t.MaxStorageBytes).HasColumnName("max_storage_bytes").IsRequired();
            b.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });

        modelBuilder.Entity<TenantAuditLog>(b =>
        {
            b.ToTable("tenant_audit_logs", "tenants");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).HasColumnName("id");
            b.Property(l => l.TenantId).HasColumnName("tenant_id").IsRequired();
            b.HasIndex(l => l.TenantId);
            b.Property(l => l.Action).HasColumnName("action").HasMaxLength(80).IsRequired();
            b.Property(l => l.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
            b.Property(l => l.PerformedBy).HasColumnName("performed_by").HasMaxLength(150).IsRequired();
            b.Property(l => l.TimestampUtc).HasColumnName("timestamp_utc").IsRequired();
        });
    }
}
