using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Billing.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Billing.Persistence;

public class BillingDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public BillingDbContext(
        DbContextOptions<BillingDbContext> options,
        ITenantContextAccessor tenantContextAccessor) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<UsageMeterRecord> UsageMeterRecords => Set<UsageMeterRecord>();
    public DbSet<BillingInvoice> BillingInvoices => Set<BillingInvoice>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("billing");

        // 1. SubscriptionPlan Entity (Global Catalog)
        modelBuilder.Entity<SubscriptionPlan>(b =>
        {
            b.ToTable("subscription_plans");
            b.HasKey(p => p.Id);

            b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(p => p.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
            b.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            b.Property(p => p.MonthlyPriceCents).HasColumnName("monthly_price_cents").IsRequired();
            b.Property(p => p.MaxAgents).HasColumnName("max_agents").IsRequired();
            b.Property(p => p.MaxMonthlyTickets).HasColumnName("max_monthly_tickets").IsRequired();
            b.Property(p => p.MaxMonthlyAiTokens).HasColumnName("max_monthly_ai_tokens").IsRequired();
            b.Property(p => p.CustomBrandingEnabled).HasColumnName("custom_branding_enabled").IsRequired();
            b.Property(p => p.WebhooksEnabled).HasColumnName("webhooks_enabled").IsRequired();
            b.Property(p => p.SlaEnabled).HasColumnName("sla_enabled").IsRequired();
            b.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();

            b.HasIndex(p => p.Code).IsUnique().HasDatabaseName("ix_subscription_plans_code");
        });

        // 2. TenantSubscription Entity (ITenantScoped)
        modelBuilder.Entity<TenantSubscription>(b =>
        {
            b.ToTable("tenant_subscriptions");
            b.HasKey(s => s.Id);

            b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(s => s.PlanCode).HasColumnName("plan_code").HasMaxLength(50).IsRequired();
            b.Property(s => s.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            b.Property(s => s.BillingCycle).HasColumnName("billing_cycle").HasMaxLength(50).IsRequired();
            b.Property(s => s.StripeCustomerId).HasColumnName("stripe_customer_id").HasMaxLength(100);
            b.Property(s => s.StripeSubscriptionId).HasColumnName("stripe_subscription_id").HasMaxLength(100);
            b.Property(s => s.CurrentPeriodStartUtc).HasColumnName("current_period_start_utc").IsRequired();
            b.Property(s => s.CurrentPeriodEndUtc).HasColumnName("current_period_end_utc").IsRequired();
            b.Property(s => s.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end").HasDefaultValue(false).IsRequired();
            b.Property(s => s.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            b.HasIndex(s => s.TenantId).IsUnique().HasDatabaseName("ix_tenant_subscriptions_tenant_id");
            b.HasIndex(s => s.StripeCustomerId).HasDatabaseName("ix_tenant_subscriptions_stripe_customer_id");
            b.HasIndex(s => s.StripeSubscriptionId).HasDatabaseName("ix_tenant_subscriptions_stripe_sub_id");

            b.HasQueryFilter(s => s.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });

        // 3. UsageMeterRecord Entity (ITenantScoped)
        modelBuilder.Entity<UsageMeterRecord>(b =>
        {
            b.ToTable("usage_meter_records");
            b.HasKey(u => u.Id);

            b.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(u => u.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(u => u.MetricName).HasColumnName("metric_name").HasMaxLength(100).IsRequired();
            b.Property(u => u.PeriodKey).HasColumnName("period_key").HasMaxLength(20).IsRequired();
            b.Property(u => u.Quantity).HasColumnName("quantity").HasDefaultValue(0L).IsRequired();
            b.Property(u => u.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            b.HasIndex(u => u.TenantId).HasDatabaseName("ix_usage_meter_records_tenant_id");
            b.HasIndex(u => new { u.TenantId, u.MetricName, u.PeriodKey }).IsUnique().HasDatabaseName("ix_usage_meter_records_tenant_metric_period");

            b.HasQueryFilter(u => u.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });

        // 4. BillingInvoice Entity (ITenantScoped)
        modelBuilder.Entity<BillingInvoice>(b =>
        {
            b.ToTable("billing_invoices");
            b.HasKey(i => i.Id);

            b.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(i => i.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(100).IsRequired();
            b.Property(i => i.StripeInvoiceId).HasColumnName("stripe_invoice_id").HasMaxLength(100);
            b.Property(i => i.PlanCode).HasColumnName("plan_code").HasMaxLength(50).IsRequired();
            b.Property(i => i.AmountCents).HasColumnName("amount_cents").IsRequired();
            b.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(10).HasDefaultValue("usd").IsRequired();
            b.Property(i => i.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            b.Property(i => i.PeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
            b.Property(i => i.PeriodEndUtc).HasColumnName("period_end_utc").IsRequired();
            b.Property(i => i.PaidAtUtc).HasColumnName("paid_at_utc");
            b.Property(i => i.HostedInvoiceUrl).HasColumnName("hosted_invoice_url").HasColumnType("text");
            b.Property(i => i.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasIndex(i => i.TenantId).HasDatabaseName("ix_billing_invoices_tenant_id");
            b.HasIndex(i => i.InvoiceNumber).IsUnique().HasDatabaseName("ix_billing_invoices_invoice_number");
            b.HasIndex(i => i.StripeInvoiceId).HasDatabaseName("ix_billing_invoices_stripe_invoice_id");

            b.HasQueryFilter(i => i.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });

        // 5. ProcessedStripeEvent Entity (Global Webhook Idempotency)
        modelBuilder.Entity<ProcessedStripeEvent>(b =>
        {
            b.ToTable("processed_stripe_events");
            b.HasKey(e => e.EventId);

            b.Property(e => e.EventId).HasColumnName("event_id").HasMaxLength(100);
            b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
            b.Property(e => e.ProcessedAtUtc).HasColumnName("processed_at_utc").IsRequired();
        });
    }
}
