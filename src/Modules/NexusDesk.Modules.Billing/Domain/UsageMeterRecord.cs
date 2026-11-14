using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Billing.Domain;

public sealed class UsageMeterRecord : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string MetricName { get; private set; }
    public string PeriodKey { get; private set; }
    public long Quantity { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public UsageMeterRecord(
        Guid id,
        Guid tenantId,
        string metricName,
        string periodKey,
        long quantity = 0,
        DateTimeOffset? updatedAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        MetricName = metricName ?? throw new ArgumentNullException(nameof(metricName));
        PeriodKey = periodKey ?? throw new ArgumentNullException(nameof(periodKey));
        Quantity = quantity;
        UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow;
    }

    private UsageMeterRecord() : base()
    {
        MetricName = string.Empty;
        PeriodKey = string.Empty;
    }

    public void Increment(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Increment amount cannot be negative.");
        }
        Quantity += amount;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetQuantity(long quantity)
    {
        Quantity = quantity;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
