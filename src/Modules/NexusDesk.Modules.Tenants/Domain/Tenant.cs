using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Domain;

public sealed class Tenant : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public string Subdomain { get; private set; }
    public TenantPlanTier PlanTier { get; private set; }
    public IsolationStrategy IsolationStrategy { get; private set; }
    public string? CustomConnectionString { get; private set; }
    public TenantStatus Status { get; private set; }
    public int MaxAgents { get; private set; }
    public int MaxTicketsPerMonth { get; private set; }
    public long MaxStorageBytes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Tenant(
        Guid id,
        string name,
        string subdomain,
        TenantPlanTier planTier,
        IsolationStrategy isolationStrategy,
        DateTimeOffset createdAtUtc) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name cannot be empty.", nameof(name));

        if (!SubdomainValidator.IsValid(subdomain, out var error))
            throw new ArgumentException(error, nameof(subdomain));

        var quotas = PlanQuotaCalculator.GetQuotas(planTier);

        Name = name.Trim();
        Subdomain = subdomain.Trim().ToLowerInvariant();
        PlanTier = planTier;
        IsolationStrategy = isolationStrategy;
        Status = TenantStatus.Active;
        MaxAgents = quotas.MaxAgents;
        MaxTicketsPerMonth = quotas.MaxTicketsPerMonth;
        MaxStorageBytes = quotas.MaxStorageBytes;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = null;
    }

    private Tenant() : base()
    {
        Name = string.Empty;
        Subdomain = string.Empty;
    }

    public void Suspend(string reason, DateTimeOffset timestampUtc)
    {
        Status = TenantStatus.Suspended;
        UpdatedAtUtc = timestampUtc;
    }

    public void Activate(string reason, DateTimeOffset timestampUtc)
    {
        Status = TenantStatus.Active;
        UpdatedAtUtc = timestampUtc;
    }

    public void SetCustomConnectionString(string encryptedConnectionString, DateTimeOffset timestampUtc)
    {
        CustomConnectionString = encryptedConnectionString;
        UpdatedAtUtc = timestampUtc;
    }
}
