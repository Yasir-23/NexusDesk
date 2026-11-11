namespace NexusDesk.SharedKernel.MultiTenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
    public string? Subdomain { get; set; }
    public TenantPlanTier? PlanTier { get; set; }
    public IsolationStrategy? IsolationStrategy { get; set; }
    public TenantStatus? Status { get; set; }
    public string? CustomConnectionString { get; set; }
    public bool IsResolved => TenantId.HasValue && !string.IsNullOrWhiteSpace(Subdomain);

    public static TenantContext Empty => new();
}
