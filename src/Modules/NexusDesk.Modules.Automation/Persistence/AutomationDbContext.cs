using System.Text.Json;
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
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DeadLetterWebhook> DeadLetterWebhooks => Set<DeadLetterWebhook>();

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

        modelBuilder.Entity<WebhookSubscription>(b =>
        {
            b.ToTable("webhook_subscriptions", "automation");
            b.HasKey(w => w.Id);

            b.Property(w => w.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(w => w.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(w => w.TargetUrl).HasColumnName("target_url").HasMaxLength(2048).IsRequired();
            b.Property(w => w.EncryptedSecret).HasColumnName("encrypted_secret").HasColumnType("text").IsRequired();
            b.Property(w => w.EventTypes).HasColumnName("event_types").HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<IReadOnlyList<string>>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
                .IsRequired();
            b.Property(w => w.IsActive).HasColumnName("is_active").IsRequired();
            b.Property(w => w.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(w => w.UpdatedAtUtc).HasColumnName("updated_at_utc");

            b.HasIndex(w => w.TenantId)
                .HasDatabaseName("ix_webhook_subscriptions_tenant_id");

            b.HasIndex(w => new { w.TenantId, w.IsActive })
                .HasDatabaseName("ix_webhook_subscriptions_tenant_active");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", "automation");
            b.HasKey(m => m.Id);

            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(m => m.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            b.Property(m => m.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            b.Property(m => m.Status).HasColumnName("status").HasMaxLength(30).HasConversion<string>().IsRequired();
            b.Property(m => m.RetryCount).HasColumnName("retry_count").HasDefaultValue(0).IsRequired();
            b.Property(m => m.NextRetryAtUtc).HasColumnName("next_retry_at_utc").IsRequired();
            b.Property(m => m.LastError).HasColumnName("last_error").HasColumnType("text");
            b.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(m => m.ProcessedAtUtc).HasColumnName("processed_at_utc");

            b.HasIndex(m => m.TenantId)
                .HasDatabaseName("ix_outbox_messages_tenant_id");

            b.HasIndex(m => new { m.TenantId, m.Status, m.NextRetryAtUtc })
                .HasDatabaseName("ix_outbox_messages_tenant_status_retry");

            b.HasIndex(m => new { m.Status, m.NextRetryAtUtc })
                .HasDatabaseName("ix_outbox_messages_status_retry");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<DeadLetterWebhook>(b =>
        {
            b.ToTable("dead_letter_webhooks", "automation");
            b.HasKey(d => d.Id);

            b.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(d => d.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(d => d.OutboxMessageId).HasColumnName("outbox_message_id").IsRequired();
            b.Property(d => d.WebhookSubscriptionId).HasColumnName("webhook_subscription_id").IsRequired();
            b.Property(d => d.TargetUrl).HasColumnName("target_url").HasMaxLength(2048).IsRequired();
            b.Property(d => d.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            b.Property(d => d.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            b.Property(d => d.AttemptCount).HasColumnName("attempt_count").IsRequired();
            b.Property(d => d.LastHttpStatusCode).HasColumnName("last_http_status_code");
            b.Property(d => d.LastError).HasColumnName("last_error").HasColumnType("text").IsRequired();
            b.Property(d => d.FailedAtUtc).HasColumnName("failed_at_utc").IsRequired();

            b.HasIndex(d => d.TenantId)
                .HasDatabaseName("ix_dead_letter_webhooks_tenant_id");

            b.HasIndex(d => d.OutboxMessageId)
                .HasDatabaseName("ix_dead_letter_webhooks_outbox_message_id");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });
    }
}
