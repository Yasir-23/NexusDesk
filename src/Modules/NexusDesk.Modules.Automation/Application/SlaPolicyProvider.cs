using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.SharedKernel.Automation;

namespace NexusDesk.Modules.Automation.Application;

public sealed class SlaPolicyProvider : ISlaPolicyProvider
{
    private readonly AutomationDbContext _automationDb;

    public SlaPolicyProvider(AutomationDbContext automationDb)
    {
        _automationDb = automationDb;
    }

    public async Task<SlaPolicyTargets?> GetPolicyTargetsAsync(
        Guid tenantId,
        string priority,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || !SlaPolicy.TryNormalizePriority(priority, out var normalizedPriority))
        {
            return null;
        }

        var policy = await _automationDb.SlaPolicies
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Priority == normalizedPriority)
            .Select(p => new { p.FirstResponseTargetMinutes, p.ResolutionTargetMinutes })
            .FirstOrDefaultAsync(cancellationToken);

        return policy == null
            ? null
            : new SlaPolicyTargets(policy.FirstResponseTargetMinutes, policy.ResolutionTargetMinutes);
    }
}
