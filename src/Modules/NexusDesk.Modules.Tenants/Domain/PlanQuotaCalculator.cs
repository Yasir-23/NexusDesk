using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Tenants.Domain;

public record PlanQuotas(int MaxAgents, int MaxTicketsPerMonth, long MaxStorageBytes);

public static class PlanQuotaCalculator
{
    public static PlanQuotas GetQuotas(TenantPlanTier planTier)
    {
        return planTier switch
        {
            TenantPlanTier.Free => new PlanQuotas(
                MaxAgents: 2,
                MaxTicketsPerMonth: 100,
                MaxStorageBytes: 500L * 1024 * 1024 // 500 MB
            ),
            TenantPlanTier.Pro => new PlanQuotas(
                MaxAgents: 15,
                MaxTicketsPerMonth: 2500,
                MaxStorageBytes: 25L * 1024 * 1024 * 1024 // 25 GB
            ),
            TenantPlanTier.Enterprise => new PlanQuotas(
                MaxAgents: 9999,
                MaxTicketsPerMonth: 1000000,
                MaxStorageBytes: 1024L * 1024 * 1024 * 1024 // 1024 GB (1 TB)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(planTier), $"Unsupported plan tier: {planTier}")
        };
    }
}
