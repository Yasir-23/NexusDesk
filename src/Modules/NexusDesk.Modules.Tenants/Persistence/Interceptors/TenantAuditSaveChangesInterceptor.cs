using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.MultiTenancy.Exceptions;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Persistence.Interceptors;

public sealed class TenantAuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public TenantAuditSaveChangesInterceptor(ITenantContextAccessor tenantContextAccessor)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyTenantAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyTenantAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyTenantAudit(DbContext? context)
    {
        if (context == null) return;

        var currentTenantId = _tenantContextAccessor.TenantContext?.TenantId;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantScoped tenantScoped)
            {
                if (entry.State == EntityState.Added)
                {
                    if (tenantScoped.TenantId == Guid.Empty)
                    {
                        if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty)
                        {
                            var prop = entry.Property(nameof(ITenantScoped.TenantId));
                            prop.CurrentValue = currentTenantId.Value;
                        }
                    }
                    else if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty && tenantScoped.TenantId != currentTenantId.Value)
                    {
                        throw new TenantIsolationViolationException(tenantScoped.TenantId, currentTenantId.Value);
                    }

                    var createdAtProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CreatedAtUtc");
                    if (createdAtProp != null && (createdAtProp.CurrentValue == null || (DateTimeOffset)createdAtProp.CurrentValue == default))
                    {
                        createdAtProp.CurrentValue = now;
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty && tenantScoped.TenantId != currentTenantId.Value)
                    {
                        throw new TenantIsolationViolationException(tenantScoped.TenantId, currentTenantId.Value);
                    }

                    var updatedAtProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAtUtc");
                    if (updatedAtProp != null)
                    {
                        updatedAtProp.CurrentValue = now;
                    }
                }
                else if (entry.State == EntityState.Deleted)
                {
                    if (currentTenantId.HasValue && currentTenantId.Value != Guid.Empty && tenantScoped.TenantId != currentTenantId.Value)
                    {
                        throw new TenantIsolationViolationException(tenantScoped.TenantId, currentTenantId.Value);
                    }
                }
            }
        }
    }
}
