using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Identity.Authorization;

public static class IdentityPolicies
{
    public const string PlatformSuperAdminOnly = "PlatformSuperAdminOnly";
    public const string TenantAdminOrHigher = "TenantAdminOrHigher";
    public const string AgentOrHigher = "AgentOrHigher";
    public const string AuthenticatedTenantUser = "AuthenticatedTenantUser";
}

public sealed class TenantBoundaryRequirement : IAuthorizationRequirement
{
    public string MinimumRole { get; }

    public TenantBoundaryRequirement(string minimumRole)
    {
        MinimumRole = minimumRole;
    }
}

public sealed class TenantBoundaryAuthorizationHandler : AuthorizationHandler<TenantBoundaryRequirement>
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public TenantBoundaryAuthorizationHandler(ITenantContextAccessor tenantContextAccessor)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantBoundaryRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
        if (string.IsNullOrWhiteSpace(role))
        {
            return Task.CompletedTask;
        }

        var isSuperAdmin = string.Equals(role, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        // Platform Super Admin passes any requirement
        if (isSuperAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // If requirement is PlatformSuperAdminOnly, non-SuperAdmin fails
        if (string.Equals(requirement.MinimumRole, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Check Tenant boundary
        var tenantContext = _tenantContextAccessor.TenantContext;
        if (tenantContext != null && tenantContext.IsResolved && tenantContext.TenantId != Guid.Empty)
        {
            if (tenantContext.Status == TenantStatus.Suspended)
            {
                return Task.CompletedTask;
            }

            var tokenTenantIdStr = user.FindFirstValue("tenant_id");
            if (Guid.TryParse(tokenTenantIdStr, out var tokenTenantId) && tokenTenantId != tenantContext.TenantId)
            {
                return Task.CompletedTask;
            }
        }

        // Check role hierarchy
        var isAllowed = requirement.MinimumRole switch
        {
            IdentityRoles.TenantAdmin => string.Equals(role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase),
            IdentityRoles.Agent => string.Equals(role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(role, IdentityRoles.Agent, StringComparison.OrdinalIgnoreCase),
            IdentityRoles.Customer => string.Equals(role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(role, IdentityRoles.Agent, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(role, IdentityRoles.Customer, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (isAllowed)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
