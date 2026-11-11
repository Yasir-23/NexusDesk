using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NexusDesk.Modules.Tenants.Caching;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Tenants.Middleware;

public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantCache tenantCache,
        ITenantContextAccessor tenantContextAccessor)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 1. Check if route is platform-global / bypassed
        if (IsPlatformGlobalRoute(path))
        {
            await _next(context);
            return;
        }

        // 2. Extract tenant identifier using precedence rules:
        //    a. X-Tenant-Id (GUID or slug)
        //    b. X-Tenant-Subdomain
        //    c. Host header subdomain
        var tenantIdentifier = ExtractTenantIdentifier(context);

        if (string.IsNullOrWhiteSpace(tenantIdentifier))
        {
            // If it's a tenant-scoped route (/api/v1/tenant/*), tenant resolution is strictly mandatory
            if (path.StartsWith("/api/v1/tenant", StringComparison.OrdinalIgnoreCase))
            {
                await WriteProblemDetailsResponse(context, HttpStatusCode.NotFound, "Tenant Not Found",
                    "A valid tenant identifier (X-Tenant-Id, X-Tenant-Subdomain, or Host subdomain) is required for this endpoint.",
                    "TENANT_NOT_FOUND");
                return;
            }

            // For other routes, proceed with unresolved context
            await _next(context);
            return;
        }

        // 3. Resolve tenant descriptor
        var descriptor = await tenantCache.GetOrSetAsync(tenantIdentifier, context.RequestAborted);

        if (descriptor == null)
        {
            await WriteProblemDetailsResponse(context, HttpStatusCode.NotFound, "Tenant Not Found",
                $"Tenant '{tenantIdentifier}' could not be resolved or does not exist.",
                "TENANT_NOT_FOUND");
            return;
        }

        // 4. Check if tenant is Suspended
        if (descriptor.Status == TenantStatus.Suspended)
        {
            await WriteProblemDetailsResponse(context, HttpStatusCode.Forbidden, "Tenant Suspended",
                $"Tenant '{descriptor.Subdomain}' is currently suspended.",
                "TENANT_SUSPENDED");
            return;
        }

        // 5. Populate ITenantContextAccessor
        var tenantContext = new TenantContext
        {
            TenantId = descriptor.TenantId,
            Subdomain = descriptor.Subdomain,
            PlanTier = descriptor.PlanTier,
            IsolationStrategy = descriptor.IsolationStrategy,
            Status = descriptor.Status,
            CustomConnectionString = descriptor.CustomConnectionString
        };

        tenantContextAccessor.TenantContext = tenantContext;

        await _next(context);
    }

    private static bool IsPlatformGlobalRoute(string path)
    {
        return path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/v1/system", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/v1/tenants/provision", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/v1/admin/tenants", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractTenantIdentifier(HttpContext context)
    {
        // 1. X-Tenant-Id header
        if (context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdHeader) && !string.IsNullOrWhiteSpace(tenantIdHeader))
        {
            return tenantIdHeader.ToString().Trim();
        }

        // 2. X-Tenant-Subdomain header
        if (context.Request.Headers.TryGetValue("X-Tenant-Subdomain", out var tenantSubdomainHeader) && !string.IsNullOrWhiteSpace(tenantSubdomainHeader))
        {
            return tenantSubdomainHeader.ToString().Trim();
        }

        // 3. Host header subdomain (e.g. acme.nexusdesk.com or acme.localhost:56100)
        var host = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(host) && host.Contains('.'))
        {
            var parts = host.Split('.');
            if (parts.Length >= 2)
            {
                var candidate = parts[0];
                // Ignore general prefixes like localhost or www
                if (!string.Equals(candidate, "localhost", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(candidate, "www", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate.Trim();
                }
            }
        }

        return null;
    }

    private static async Task WriteProblemDetailsResponse(
        HttpContext context,
        HttpStatusCode statusCode,
        string title,
        string detail,
        string errorCode)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Type = $"https://nexusdesk.io/errors/{errorCode.ToLowerInvariant().Replace('_', '-')}",
            Title = title,
            Status = (int)statusCode,
            Detail = detail,
            Instance = context.Request.Path
        };
        problemDetails.Extensions["errorCode"] = errorCode;

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
