using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Identity.Authorization;

public sealed class TenantClaimMatchMiddleware
{
    private readonly RequestDelegate _next;

    public TenantClaimMatchMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
            var isSuperAdmin = string.Equals(role, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

            var tenantContext = tenantContextAccessor.TenantContext;

            if (!isSuperAdmin && tenantContext != null && tenantContext.IsResolved && tenantContext.TenantId != Guid.Empty)
            {
                // Check 1: Tenant Suspended Gate (even if JWT was issued prior to suspension)
                if (tenantContext.Status == TenantStatus.Suspended)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://nexusdesk.io/errors/tenant-suspended",
                        title = "Tenant Suspended",
                        status = StatusCodes.Status403Forbidden,
                        detail = $"Tenant '{tenantContext.Subdomain}' is currently suspended. Access is blocked.",
                        code = "TENANT_SUSPENDED",
                        errorCode = "TENANT_SUSPENDED"
                    }, context.RequestAborted);
                    return;
                }

                // Check 2: Cross-Tenant Token Boundary
                var tokenTenantIdStr = user.FindFirstValue("tenant_id");
                if (!string.IsNullOrWhiteSpace(tokenTenantIdStr) && !string.Equals(tokenTenantIdStr, "platform", StringComparison.OrdinalIgnoreCase))
                {
                    if (Guid.TryParse(tokenTenantIdStr, out var tokenTenantId))
                    {
                        if (tokenTenantId != tenantContext.TenantId)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/problem+json";
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "https://nexusdesk.io/errors/cross-tenant-access-denied",
                                title = "Cross-Tenant Access Denied",
                                status = StatusCodes.Status403Forbidden,
                                detail = $"Token tenant '{tokenTenantId}' does not match request tenant boundary '{tenantContext.TenantId}'.",
                                code = "CROSS_TENANT_ACCESS_DENIED",
                                errorCode = "CROSS_TENANT_ACCESS_DENIED"
                            }, context.RequestAborted);
                            return;
                        }
                    }
                }
            }
        }

        await _next(context);
    }
}
