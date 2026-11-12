using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Ticketing.Application.Commands;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Application.Queries;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.Modules.Ticketing.RealTime;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Endpoints;

public static class TicketingEndpoints
{
    public static IEndpointRouteBuilder MapTicketingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tickets")
            .WithTags("Ticketing");

        // 1. POST /api/v1/tickets — Create ticket
        group.MapPost("/", async (
            [FromBody] CreateTicketRequest request,
            CreateTicketCommandHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isCustomer))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var command = new CreateTicketCommand(
                request.Title,
                request.Description,
                request.Priority,
                request.Channel,
                request.CustomerId,
                callerUserId,
                isCustomer,
                request.Tags);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblemResult(result.Error, httpContext);
            }

            return Results.Created($"/api/v1/tickets/{result.Value.Id}", result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<CreateTicketRequest>("application/json")
        .Produces<TicketDetailsResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("CreateTicket");

        // 2. GET /api/v1/tickets — Get paginated tickets
        group.MapGet("/", async (
            [FromQuery] TicketStatus? status,
            [FromQuery] TicketPriority? priority,
            [FromQuery] Guid? assignedAgentId,
            [FromQuery] Guid? customerId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            GetTicketsPagedQueryHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isCustomer))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var query = new GetTicketsPagedQuery(
                status,
                priority,
                assignedAgentId,
                customerId,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 20 : pageSize,
                callerUserId,
                isCustomer);

            var result = await handler.HandleAsync(query, cancellationToken);
            return result.IsFailure
                ? MapErrorToProblemResult(result.Error, httpContext)
                : Results.Ok(result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<PagedTicketsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetTicketsPaged");

        // 3. GET /api/v1/tickets/{id} — Get ticket by ID
        group.MapGet("/{id:guid}", async (
            Guid id,
            GetTicketByIdQueryHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isCustomer))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var result = await handler.HandleAsync(new GetTicketByIdQuery(id, callerUserId, isCustomer), cancellationToken);
            return result.IsFailure
                ? MapErrorToProblemResult(result.Error, httpContext)
                : Results.Ok(result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<TicketDetailsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("GetTicketById");

        // 4. PATCH /api/v1/tickets/{id}/assign — Assign to agent
        group.MapPatch("/{id:guid}/assign", async (
            Guid id,
            [FromBody] AssignTicketRequest request,
            AssignTicketCommandHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var command = new AssignTicketCommand(id, request.AgentId, callerUserId, request.RowVersion);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.IsFailure
                ? MapErrorToProblemResult(result.Error, httpContext)
                : Results.Ok(result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AgentOrHigher)
        .Accepts<AssignTicketRequest>("application/json")
        .Produces<TicketDetailsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("AssignTicket");

        // 5. PATCH /api/v1/tickets/{id}/status — Transition status
        group.MapPatch("/{id:guid}/status", async (
            Guid id,
            [FromBody] UpdateTicketStatusRequest request,
            UpdateTicketStatusCommandHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var command = new UpdateTicketStatusCommand(
                id,
                request.Status,
                request.ResolutionNotes,
                request.Reason,
                callerUserId,
                request.RowVersion);

            var result = await handler.HandleAsync(command, cancellationToken);
            return result.IsFailure
                ? MapErrorToProblemResult(result.Error, httpContext)
                : Results.Ok(result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AgentOrHigher)
        .Accepts<UpdateTicketStatusRequest>("application/json")
        .Produces<TicketDetailsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("UpdateTicketStatus");

        // 6. PATCH /api/v1/tickets/{id}/priority — Change priority
        group.MapPatch("/{id:guid}/priority", async (
            Guid id,
            [FromBody] UpdateTicketPriorityRequest request,
            UpdateTicketPriorityCommandHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var command = new UpdateTicketPriorityCommand(id, request.Priority, callerUserId, request.RowVersion);
            var result = await handler.HandleAsync(command, cancellationToken);
            return result.IsFailure
                ? MapErrorToProblemResult(result.Error, httpContext)
                : Results.Ok(result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AgentOrHigher)
        .Accepts<UpdateTicketPriorityRequest>("application/json")
        .Produces<TicketDetailsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("UpdateTicketPriority");

        // 7. POST /api/v1/tickets/{id}/comments — Add comment
        group.MapPost("/{id:guid}/comments", async (
            Guid id,
            [FromBody] AddCommentRequest request,
            AddTicketCommentCommandHandler handler,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isCustomer))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "TICKET_UNAUTHORIZED",
                    httpContext);
            }

            var command = new AddTicketCommentCommand(
                id,
                request.Body,
                request.Type,
                callerUserId,
                isCustomer);

            var result = await handler.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                return MapErrorToProblemResult(result.Error, httpContext);
            }

            return Results.Created($"/api/v1/tickets/{id}/comments/{result.Value.Id}", result.Value);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<AddCommentRequest>("application/json")
        .Produces<TicketCommentDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("AddTicketComment");

        // 8. GET /api/v1/tickets/{id}/presence — Active staff viewers and typing indicators
        group.MapGet("/{id:guid}/presence", async (
            Guid id,
            TicketingDbContext ticketingDb,
            ITicketPresenceTracker presenceTracker,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var exists = await ticketingDb.Tickets
                .AsNoTracking()
                .AnyAsync(t => t.Id == id && t.TenantId == tenantId, cancellationToken);

            if (!exists)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Ticket '{id}' was not found.",
                    "Ticket.NotFound",
                    httpContext);
            }

            var activeAgents = presenceTracker.GetActiveAgents(tenantId, id);
            return Results.Ok(new TicketPresenceResponse(
                id,
                tenantId,
                activeAgents,
                DateTimeOffset.UtcNow));
        })
        .RequireAuthorization(IdentityPolicies.AgentOrHigher)
        .Produces<TicketPresenceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("GetTicketPresence");

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
                "A valid tenant identifier (X-Tenant-Subdomain, X-Tenant-Id, or Host subdomain) is required for ticketing endpoints.",
                "TENANT_NOT_RESOLVED",
                httpContext);
            return false;
        }

        errorResult = null;
        return true;
    }

    private static bool TryGetCallerIdentity(ClaimsPrincipal user, out Guid userId, out bool isCustomer)
    {
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? user.FindFirstValue("sub");

        var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
        isCustomer = string.Equals(role, IdentityRoles.Customer, StringComparison.OrdinalIgnoreCase);

        return Guid.TryParse(userIdStr, out userId) && userId != Guid.Empty;
    }

    private static IResult MapErrorToProblemResult(Error error, HttpContext httpContext)
    {
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
