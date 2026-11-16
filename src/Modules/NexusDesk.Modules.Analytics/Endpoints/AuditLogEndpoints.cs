using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Analytics.Contracts;
using NexusDesk.Modules.Analytics.Domain;
using NexusDesk.Modules.Analytics.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Analytics.Endpoints;

public static class AuditLogEndpoints
{
    public static IEndpointRouteBuilder MapAuditLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit-logs")
            .WithTags("Audit Logs")
            .RequireAuthorization("audit:read");

        // 1. GET /api/v1/audit-logs?resourceType=&page=1&pageSize=20
        group.MapGet("/", async (
            [FromQuery] string? resourceType,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            AnalyticsDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            var query = dbContext.AuditLogs
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId);

            if (!string.IsNullOrWhiteSpace(resourceType))
            {
                query = query.Where(a => a.ResourceType.ToLower() == resourceType.ToLower());
            }

            var currentPage = page is null or <= 0 ? 1 : page.Value;
            var take = pageSize is null or <= 0 ? 20 : Math.Min(pageSize.Value, 100);

            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = (int)Math.Ceiling(totalCount / (double)take);

            var items = await query
                .OrderByDescending(a => a.CreatedAtUtc)
                .Skip((currentPage - 1) * take)
                .Take(take)
                .Select(a => new AuditLogEntryResponse(
                    a.Id,
                    a.TenantId,
                    a.ActorUserId,
                    a.ActorEmail,
                    a.Action,
                    a.ResourceType,
                    a.ResourceId,
                    a.MetadataJson,
                    a.IpAddress,
                    a.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new PagedAuditLogsResponse(items, currentPage, take, totalCount, totalPages));
        })
        .Produces<PagedAuditLogsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetAuditLogs");

        // 2. POST /api/v1/audit-logs
        group.MapPost("/", async (
            [FromBody] CreateAuditLogRequest request,
            AnalyticsDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            if (string.IsNullOrWhiteSpace(request.Action) ||
                string.IsNullOrWhiteSpace(request.ResourceType) ||
                string.IsNullOrWhiteSpace(request.ResourceId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Failed",
                    detail: "Action, ResourceType, and ResourceId are required fields.");
            }

            var actorUserId = TryGetCallerUserId(httpContext.User);
            var actorEmail = httpContext.User.FindFirstValue(ClaimTypes.Email)
                             ?? httpContext.User.FindFirstValue("email");

            var ip = request.IpAddress ?? httpContext.Connection.RemoteIpAddress?.ToString();

            var entry = new AuditLogEntry(
                Guid.NewGuid(),
                tenantId,
                request.Action,
                request.ResourceType,
                request.ResourceId,
                actorUserId,
                actorEmail,
                request.MetadataJson,
                ip,
                DateTimeOffset.UtcNow);

            dbContext.AuditLogs.Add(entry);
            await dbContext.SaveChangesAsync(cancellationToken);

            var response = new AuditLogEntryResponse(
                entry.Id,
                entry.TenantId,
                entry.ActorUserId,
                entry.ActorEmail,
                entry.Action,
                entry.ResourceType,
                entry.ResourceId,
                entry.MetadataJson,
                entry.IpAddress,
                entry.CreatedAtUtc);

            return Results.Created($"/api/v1/audit-logs/{entry.Id}", response);
        })
        .Accepts<CreateAuditLogRequest>("application/json")
        .Produces<AuditLogEntryResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("CreateAuditLog");

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

    private static Guid? TryGetCallerUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var g) && g != Guid.Empty ? g : null;
    }
}
