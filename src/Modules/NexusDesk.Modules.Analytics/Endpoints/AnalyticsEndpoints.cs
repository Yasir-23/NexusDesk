using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NexusDesk.Modules.Analytics.Services;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.SharedKernel.Analytics;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Analytics.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics")
            .WithTags("Analytics & Reporting")
            .RequireAuthorization("analytics:read");

        // 1. GET /api/v1/analytics/overview?days=30
        group.MapGet("/overview", async (
            [FromQuery] int? days,
            IAnalyticsService analyticsService,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            var daysCount = days is null or <= 0 ? 30 : days.Value;
            var overview = await analyticsService.GetOverviewAsync(tenantId, daysCount, cancellationToken);
            return Results.Ok(overview);
        })
        .Produces<TenantAnalyticsOverviewDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetAnalyticsOverview");

        // 2. GET /api/v1/analytics/tickets/trends?days=14
        group.MapGet("/tickets/trends", async (
            [FromQuery] int? days,
            IAnalyticsService analyticsService,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            var daysCount = days is null or <= 0 ? 14 : days.Value;
            var trends = await analyticsService.GetTicketTrendsAsync(tenantId, daysCount, cancellationToken);
            return Results.Ok(trends);
        })
        .Produces<IReadOnlyList<DailyTicketTrendDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetTicketTrends");

        // 3. GET /api/v1/analytics/agents/performance?days=30
        group.MapGet("/agents/performance", async (
            [FromQuery] int? days,
            IAnalyticsService analyticsService,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            var daysCount = days is null or <= 0 ? 30 : days.Value;
            var performance = await analyticsService.GetAgentPerformanceAsync(tenantId, daysCount, cancellationToken);
            return Results.Ok(performance);
        })
        .Produces<IReadOnlyList<AgentPerformanceDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetAgentPerformance");

        // 4. GET /api/v1/analytics/export/tickets.csv?days=30
        group.MapGet("/export/tickets.csv", async (
            [FromQuery] int? days,
            IAnalyticsService analyticsService,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            var daysCount = days is null or <= 0 ? 30 : days.Value;
            var csvContent = await analyticsService.ExportTicketsCsvAsync(tenantId, daysCount, cancellationToken);

            httpContext.Response.Headers.Append("Content-Disposition", "attachment; filename=\"nexusdesk-tickets-report.csv\"");
            return Results.Content(csvContent, "text/csv", Encoding.UTF8);
        })
        .Produces(StatusCodes.Status200OK, contentType: "text/csv")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("ExportTicketsCsv");

        return app;
    }

    private static bool TryEnsureTenantResolved(
        ITenantContextAccessor tenantContextAccessor,
        out Guid tenantId,
        out IResult? errorResult)
    {
        tenantId = Guid.Empty;
        var context = tenantContextAccessor.TenantContext;
        if (context == null || !context.IsResolved || !context.TenantId.HasValue || context.TenantId.Value == Guid.Empty)
        {
            errorResult = Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Tenant Not Resolved",
                detail: "A valid tenant context header or subdomain is required.");
            return false;
        }

        tenantId = context.TenantId.Value;
        errorResult = null;
        return true;
    }
}
