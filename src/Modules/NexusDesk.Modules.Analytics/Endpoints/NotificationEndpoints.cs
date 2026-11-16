using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Analytics.Contracts;
using NexusDesk.Modules.Analytics.Persistence;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Analytics.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            .WithTags("In-App Notifications")
            .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser);

        // 1. GET /api/v1/notifications?unreadOnly=false&page=1&pageSize=20
        group.MapGet("/", async (
            [FromQuery] bool? unreadOnly,
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

            if (!TryGetCallerUserId(httpContext.User, out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Authenticated user identity claim is missing or invalid.");
            }

            var baseQuery = dbContext.Notifications
                .AsNoTracking()
                .Where(n => n.TenantId == tenantId && n.RecipientUserId == userId);

            var unreadCount = await baseQuery.CountAsync(n => !n.IsRead, cancellationToken);

            var filteredQuery = baseQuery;
            if (unreadOnly == true)
            {
                filteredQuery = filteredQuery.Where(n => !n.IsRead);
            }

            var currentPage = page is null or <= 0 ? 1 : page.Value;
            var take = pageSize is null or <= 0 ? 20 : Math.Min(pageSize.Value, 100);

            var totalCount = await filteredQuery.CountAsync(cancellationToken);
            var totalPages = (int)Math.Ceiling(totalCount / (double)take);

            var items = await filteredQuery
                .OrderByDescending(n => n.CreatedAtUtc)
                .Skip((currentPage - 1) * take)
                .Take(take)
                .Select(n => new UserNotificationResponse(
                    n.Id,
                    n.TenantId,
                    n.RecipientUserId,
                    n.Type,
                    n.Title,
                    n.Message,
                    n.LinkUrl,
                    n.IsRead,
                    n.ReadAtUtc,
                    n.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new PagedNotificationsResponse(items, unreadCount, currentPage, take, totalCount, totalPages));
        })
        .Produces<PagedNotificationsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithName("GetNotifications");

        // 2. POST /api/v1/notifications/{id}/read
        group.MapPost("/{id:guid}/read", async (
            [FromRoute] Guid id,
            AnalyticsDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            if (!TryGetCallerUserId(httpContext.User, out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Authenticated user identity claim is missing or invalid.");
            }

            var notification = await dbContext.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenantId && n.RecipientUserId == userId, cancellationToken);

            if (notification == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Notification Not Found",
                    detail: $"Notification '{id}' was not found for the current user.");
            }

            notification.MarkAsRead();
            await dbContext.SaveChangesAsync(cancellationToken);

            var response = new UserNotificationResponse(
                notification.Id,
                notification.TenantId,
                notification.RecipientUserId,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.LinkUrl,
                notification.IsRead,
                notification.ReadAtUtc,
                notification.CreatedAtUtc);

            return Results.Ok(response);
        })
        .Produces<UserNotificationResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("MarkNotificationRead");

        // 3. POST /api/v1/notifications/read-all
        group.MapPost("/read-all", async (
            AnalyticsDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            if (!TryGetCallerUserId(httpContext.User, out var userId))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Authenticated user identity claim is missing or invalid.");
            }

            var unreadNotifications = await dbContext.Notifications
                .Where(n => n.TenantId == tenantId && n.RecipientUserId == userId && !n.IsRead)
                .ToListAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var n in unreadNotifications)
            {
                n.MarkAsRead(now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(new MarkAllNotificationsReadResponse(unreadNotifications.Count));
        })
        .Produces<MarkAllNotificationsReadResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithName("MarkAllNotificationsRead");

        // 4. POST /api/v1/notifications
        group.MapPost("/", async (
            [FromBody] CreateNotificationRequest request,
            NexusDesk.SharedKernel.Notifications.INotificationService notificationService,
            ITenantContextAccessor tenantContextAccessor,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, out var tenantId, out var errorResult))
            {
                return errorResult!;
            }

            if (request.RecipientUserId == Guid.Empty)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "RecipientUserId cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Validation Error",
                    detail: "Type, Title, and Message are required.");
            }

            var id = await notificationService.CreateNotificationAsync(
                tenantId,
                request.RecipientUserId,
                request.Type,
                request.Title,
                request.Message,
                request.LinkUrl,
                cancellationToken);

            return Results.Created($"/api/v1/notifications", new { id });
        })
        .RequireAuthorization(IdentityPolicies.TenantAdminOrHigher)
        .Produces(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("CreateNotification");

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

    private static bool TryGetCallerUserId(ClaimsPrincipal user, out Guid userId)
    {
        userId = Guid.Empty;
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out userId) && userId != Guid.Empty;
    }
}
