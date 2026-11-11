using FluentAssertions;
using NexusDesk.Modules.Tenants.Domain;
using NexusDesk.SharedKernel.MultiTenancy;
using Xunit;

namespace NexusDesk.UnitTests.MultiTenancy;

public class PlanQuotaCalculationTests
{
    [Fact]
    public void FreeTier_ShouldHaveExactQuotas()
    {
        var quotas = PlanQuotaCalculator.GetQuotas(TenantPlanTier.Free);

        quotas.MaxAgents.Should().Be(2);
        quotas.MaxTicketsPerMonth.Should().Be(100);
        quotas.MaxStorageBytes.Should().Be(500L * 1024 * 1024); // 500 MB
    }

    [Fact]
    public void ProTier_ShouldHaveExactQuotas()
    {
        var quotas = PlanQuotaCalculator.GetQuotas(TenantPlanTier.Pro);

        quotas.MaxAgents.Should().Be(15);
        quotas.MaxTicketsPerMonth.Should().Be(2500);
        quotas.MaxStorageBytes.Should().Be(25L * 1024 * 1024 * 1024); // 25 GB
    }

    [Fact]
    public void EnterpriseTier_ShouldHaveExactQuotas()
    {
        var quotas = PlanQuotaCalculator.GetQuotas(TenantPlanTier.Enterprise);

        quotas.MaxAgents.Should().Be(9999);
        quotas.MaxTicketsPerMonth.Should().Be(1000000);
        quotas.MaxStorageBytes.Should().Be(1024L * 1024 * 1024 * 1024); // 1024 GB (1 TB)
    }

    [Fact]
    public void TenantConstructor_ShouldEnforceQuotasAndValidSubdomain()
    {
        var tenant = new Tenant(
            Guid.NewGuid(),
            "Acme Corp",
            "acme",
            TenantPlanTier.Pro,
            IsolationStrategy.SharedDatabase,
            DateTimeOffset.UtcNow);

        tenant.Name.Should().Be("Acme Corp");
        tenant.Subdomain.Should().Be("acme");
        tenant.PlanTier.Should().Be(TenantPlanTier.Pro);
        tenant.IsolationStrategy.Should().Be(IsolationStrategy.SharedDatabase);
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.MaxAgents.Should().Be(15);
        tenant.MaxTicketsPerMonth.Should().Be(2500);
        tenant.MaxStorageBytes.Should().Be(25L * 1024 * 1024 * 1024);
    }
}
