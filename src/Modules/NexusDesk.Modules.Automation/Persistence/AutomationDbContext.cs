using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Automation.Persistence;

public class AutomationDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public Guid CurrentTenantId => _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;

    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();

    public AutomationDbContext(
        DbContextOptions<AutomationDbContext> options,
        ITenantContextAccessor tenantContextAccessor) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("automation");

        modelBuilder.Entity<SlaPolicy>(b =>
        {
            b.ToTable("sla_policies", "automation");
            b.HasKey(p => p.Id);

            b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(p => p.Priority).HasColumnName("priority").HasMaxLength(20).IsRequired();
            b.Property(p => p.FirstResponseTargetMinutes).HasColumnName("first_response_target_minutes").IsRequired();
            b.Property(p => p.ResolutionTargetMinutes).HasColumnName("resolution_target_minutes").IsRequired();
            b.Property(p => p.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(p => p.UpdatedAtUtc).HasColumnName("updated_at_utc");

            b.HasIndex(p => p.TenantId)
                .HasDatabaseName("ix_sla_policies_tenant_id");

            b.HasIndex(p => new { p.TenantId, p.Priority })
                .IsUnique()
                .HasDatabaseName("ix_sla_policies_tenant_priority");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AutomationRule>(b =>
        {
            b.ToTable("automation_rules", "automation");
            b.HasKey(r => r.Id);

            b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(r => r.Name).HasColumnName("name").HasMaxLength(AutomationRule.MaxNameLength).IsRequired();
            b.Property(r => r.TriggerEvent).HasColumnName("trigger_event").HasMaxLength(80).IsRequired();
            b.Property(r => r.ConditionJson).HasColumnName("condition_json").HasColumnType("jsonb").IsRequired();
            b.Property(r => r.ActionType).HasColumnName("action_type").HasMaxLength(80).IsRequired();
            b.Property(r => r.ActionPayloadJson).HasColumnName("action_payload_json").HasColumnType("jsonb").IsRequired();
            b.Property(r => r.IsEnabled).HasColumnName("is_enabled").IsRequired();
            b.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(r => r.UpdatedAtUtc).HasColumnName("updated_at_utc");

            b.HasIndex(r => new { r.TenantId, r.TriggerEvent, r.IsEnabled })
                .HasDatabaseName("ix_automation_rules_tenant_trigger_enabled");

            b.HasIndex(r => r.TenantId)
                .HasDatabaseName("ix_automation_rules_tenant_id");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });
    }
}
