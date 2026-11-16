using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Ticketing.Application;
using NexusDesk.Modules.Ticketing.Application.Commands;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Endpoints;

public sealed record PortalCreateTicketRequest(
    string Title,
    string Description,
    TicketPriority Priority = TicketPriority.Medium,
    IReadOnlyList<string>? Tags = null);

public sealed record PortalTicketReplyRequest(
    string? Message = null,
    string? Body = null)
{
    public string Content => Message ?? Body ?? string.Empty;
}

public static class PortalEndpoints
{
    public static IEndpointRouteBuilder MapPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/portal")
            .WithTags("Customer Portal");

        // 1. GET /api/v1/portal/tickets — Returns paginated tickets belonging strictly to the customer
        group.MapGet("/tickets", async (
            [FromQuery] TicketStatus? status,
            [FromQuery] TicketPriority? priority,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            TicketingDbContext ticketingDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCustomerIdentity(httpContext, out var customerId, out var authError))
            {
                return authError!;
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var query = ticketingDb.Tickets
                .AsNoTracking()
                .Include(t => t.Tags)
                .Where(t => t.TenantId == tenantId && t.CustomerId == customerId);

            if (status.HasValue)
            {
                query = query.Where(t => t.Status == status.Value);
            }

            if (priority.HasValue)
            {
                query = query.Where(t => t.Priority == priority.Value);
            }

            var currentPage = page is null or <= 0 ? 1 : page.Value;
            var take = pageSize is null or <= 0 ? 20 : pageSize.Value;

            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = (int)Math.Ceiling(totalCount / (double)take);

            var items = await query
                .OrderByDescending(t => t.CreatedAtUtc)
                .Skip((currentPage - 1) * take)
                .Take(take)
                .ToListAsync(cancellationToken);

            var summaries = items.Select(t => t.ToSummaryResponse()).ToList();
            return Results.Ok(new PagedTicketsResponse(summaries, currentPage, take, totalCount, totalPages));
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<PagedTicketsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("PortalGetTickets");

        // 2. POST /api/v1/portal/tickets — Create a support ticket from the portal
        group.MapPost("/tickets", async (
            [FromBody] PortalCreateTicketRequest request,
            CreateTicketCommandHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCustomerIdentity(httpContext, out var customerId, out var authError))
            {
                return authError!;
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Ticket title is required.",
                    "PORTAL_TITLE_REQUIRED",
                    httpContext);
            }

            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Ticket description is required.",
                    "PORTAL_DESCRIPTION_REQUIRED",
                    httpContext);
            }

            var command = new CreateTicketCommand(
                request.Title,
                request.Description,
                request.Priority,
                TicketChannel.WebPortal,
                customerId,
                customerId,
                true, // IsCustomerCaller = true
                request.Tags);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblemResult(result.Error, httpContext);
            }

            return Results.Created($"/api/v1/portal/tickets/{result.Value.Id}", result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<PortalCreateTicketRequest>("application/json")
        .Produces<TicketDetailsResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("PortalCreateTicket");

        // 3. GET /api/v1/portal/tickets/{id} — Returns ticket details for the owning customer
        group.MapGet("/tickets/{id:guid}", async (
            Guid id,
            TicketingDbContext ticketingDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCustomerIdentity(httpContext, out var customerId, out var authError))
            {
                return authError!;
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticket = await ticketingDb.Tickets
                .AsNoTracking()
                .AsSplitQuery()
                .Include(t => t.Comments)
                .Include(t => t.Tags)
                .Include(t => t.ActivityLogs)
                .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

            // Critical security requirement: 404 if ticket belongs to another customer or another tenant
            if (ticket == null || ticket.CustomerId != customerId)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Ticket '{id}' was not found.",
                    "Ticket.NotFound",
                    httpContext);
            }

            // ToDetailsResponse(isCustomer: true) filters Comments to PublicReply only and blanks out ActivityLogs
            var response = ticket.ToDetailsResponse(isCustomer: true);
            return Results.Ok(response);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<TicketDetailsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("PortalGetTicketById");

        // 4. POST /api/v1/portal/tickets/{id}/replies — Customer adds a reply
        group.MapPost("/tickets/{id:guid}/replies", async (
            Guid id,
            [FromBody] PortalTicketReplyRequest request,
            AddTicketCommentCommandHandler handler,
            TicketingDbContext ticketingDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCustomerIdentity(httpContext, out var customerId, out var authError))
            {
                return authError!;
            }

            var replyText = request.Content;
            if (string.IsNullOrWhiteSpace(replyText))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Reply message content cannot be empty.",
                    "PORTAL_REPLY_EMPTY",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticketExists = await ticketingDb.Tickets
                .AsNoTracking()
                .AnyAsync(t => t.Id == id && t.TenantId == tenantId && t.CustomerId == customerId, cancellationToken);

            if (!ticketExists)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Ticket '{id}' was not found.",
                    "Ticket.NotFound",
                    httpContext);
            }

            var command = new AddTicketCommentCommand(
                id,
                replyText,
                CommentType.PublicReply,
                customerId,
                true);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblemResult(result.Error, httpContext);
            }

            return Results.Created($"/api/v1/portal/tickets/{id}/replies/{result.Value.Id}", result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<PortalTicketReplyRequest>("application/json")
        .Produces<TicketCommentDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("PortalAddTicketReply");

        return app;
    }

    private static bool TryEnsureTenantResolved(
        ITenantContextAccessor accessor,
        HttpContext httpContext,
        out IResult? errorResult)
    {
        var tenantContext = accessor.TenantContext;
        if (tenantContext == null || !tenantContext.IsResolved || !tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            errorResult = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                "Tenant Not Resolved",
                "A valid tenant identifier (X-Tenant-Subdomain, X-Tenant-Id, or Host subdomain) is required.",
                "TENANT_NOT_RESOLVED",
                httpContext);
            return false;
        }

        errorResult = null;
        return true;
    }

    private static bool TryGetCustomerIdentity(
        HttpContext httpContext,
        out Guid customerId,
        out IResult? errorResult)
    {
        var user = httpContext.User;
        var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");

        if (!string.Equals(role, IdentityRoles.Customer, StringComparison.OrdinalIgnoreCase))
        {
            customerId = Guid.Empty;
            errorResult = CreateProblemResult(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Only authenticated customers can access the customer self-service portal.",
                "PORTAL_FORBIDDEN",
                httpContext);
            return false;
        }

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out customerId) || customerId == Guid.Empty)
        {
            errorResult = CreateProblemResult(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Authenticated customer identity claim is missing or invalid.",
                "CUSTOMER_UNAUTHORIZED",
                httpContext);
            return false;
        }

        errorResult = null;
        return true;
    }

    private static IResult MapErrorToProblemResult(Error error, HttpContext httpContext)
    {
        if (error.Code.StartsWith("Quota.", StringComparison.OrdinalIgnoreCase))
        {
            return CreateProblemResult(StatusCodes.Status429TooManyRequests, "Plan Quota Exceeded", error.Description, "quota_exceeded", httpContext);
        }

        var (statusCode, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Validation Failed"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Resource Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Concurrency or State Conflict"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            _ => (StatusCodes.Status422UnprocessableEntity, "Operation Failed")
        };

        return CreateProblemResult(statusCode, title, error.Description, error.Code, httpContext);
    }

    private static IResult CreateProblemResult(
        int statusCode,
        string title,
        string detail,
        string code,
        HttpContext httpContext)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://nexusdesk.io/errors/{code.ToLowerInvariant().Replace('.', '-').Replace('_', '-')}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["errorCode"] = code;

        return Results.Problem(
            detail: problemDetails.Detail,
            instance: problemDetails.Instance,
            statusCode: statusCode,
            title: problemDetails.Title,
            type: problemDetails.Type,
            extensions: problemDetails.Extensions);
    }
}
