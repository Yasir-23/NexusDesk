using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Billing.Domain;
using NexusDesk.Modules.Billing.Persistence;
using NexusDesk.SharedKernel.Billing;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Billing.Services;

public sealed class BillingQuotaService : IBillingQuotaService
{
    private readonly BillingDbContext _dbContext;

    public BillingQuotaService(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public static long GetMetricLimit(SubscriptionPlan plan, string metricName)
    {
        return metricName.ToLowerInvariant() switch
        {
            "tickets_created" => plan.MaxMonthlyTickets,
            "active_agents" => plan.MaxAgents,
            "ai_tokens_used" => plan.MaxMonthlyAiTokens,
            "webhook_deliveries" => long.MaxValue,
            _ => long.MaxValue
        };
    }

    public static string? ValidateDowngrade(SubscriptionPlan targetPlan, IReadOnlyList<UsageMeterRecord> currentUsage)
    {
        foreach (var record in currentUsage)
        {
            var metric = record.MetricName.ToLowerInvariant();
            var limit = GetMetricLimit(targetPlan, metric);
            if (limit != long.MaxValue && record.Quantity > limit)
            {
                return $"Current usage for '{record.MetricName}' ({record.Quantity}) exceeds the target plan limit ({limit}).";
            }
        }
        return null;
    }

    [AllowCrossTenantQuery("Quota check across tenant context boundary when requested by internal service")]
    public async Task<bool> CanRecordUsageAsync(Guid tenantId, string metricName, long quantity, CancellationToken cancellationToken = default)
    {
        var sub = await _dbContext.TenantSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (sub == null)
        {
            return true;
        }

        var plan = await _dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == sub.PlanCode, cancellationToken);

        if (plan == null)
        {
            return true;
        }

        var limit = GetMetricLimit(plan, metricName);
        if (limit == long.MaxValue)
        {
            return true;
        }

        var periodKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        var record = await _dbContext.UsageMeterRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.MetricName == metricName && u.PeriodKey == periodKey, cancellationToken);

        var current = record?.Quantity ?? 0L;
        return (current + quantity) <= limit;
    }

    [AllowCrossTenantQuery("Usage recording across tenant context boundary when requested by internal service")]
    public async Task RecordUsageAsync(Guid tenantId, string metricName, long quantity, CancellationToken cancellationToken = default)
    {
        var periodKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        var record = await _dbContext.UsageMeterRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.MetricName == metricName && u.PeriodKey == periodKey, cancellationToken);

        if (record == null)
        {
            record = new UsageMeterRecord(Guid.NewGuid(), tenantId, metricName, periodKey, quantity, DateTimeOffset.UtcNow);
            _dbContext.UsageMeterRecords.Add(record);
        }
        else
        {
            record.Increment(quantity);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
