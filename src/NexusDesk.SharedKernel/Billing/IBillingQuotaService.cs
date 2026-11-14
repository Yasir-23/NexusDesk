namespace NexusDesk.SharedKernel.Billing;

public interface IBillingQuotaService
{
    Task<bool> CanRecordUsageAsync(Guid tenantId, string metricName, long quantity, CancellationToken cancellationToken = default);
    Task RecordUsageAsync(Guid tenantId, string metricName, long quantity, CancellationToken cancellationToken = default);
}
