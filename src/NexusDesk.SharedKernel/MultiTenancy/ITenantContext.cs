namespace NexusDesk.SharedKernel.MultiTenancy;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? Subdomain { get; }
    TenantPlanTier? PlanTier { get; }
    IsolationStrategy? IsolationStrategy { get; }
    TenantStatus? Status { get; }
    string? CustomConnectionString { get; }
    bool IsResolved { get; }
}
