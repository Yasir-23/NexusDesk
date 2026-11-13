using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Chat.Contracts;
using NexusDesk.Modules.Chat.Domain;
using NexusDesk.Modules.Chat.Persistence;
using NexusDesk.Modules.Chat.RealTime;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;
using NexusDesk.SharedKernel.Ticketing;

namespace NexusDesk.Modules.Chat.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/chat")
            .WithTags("Omnichannel Live Chat");

        // 1. POST /api/v1/chat/sessions (Customer: starts a new live chat session)
        group.MapPost("/sessions", async (
            [FromBody] CreateChatSessionRequest request,
            ChatDbContext chatDb,
            ITenantContextAccessor tenantContextAccessor,
            IHubContext<ChatHub, IChatHubClient> hubContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isStaff, out var role))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "CHAT_UNAUTHORIZED",
                    httpContext);
            }

            if (string.IsNullOrWhiteSpace(request.Subject))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Chat session subject is required.",
                    "CHAT_SUBJECT_REQUIRED",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var sessionResult = ChatSession.Create(tenantId, callerUserId, request.Subject);
            if (sessionResult.IsFailure)
            {
                return MapErrorToProblemResult(sessionResult.Error, httpContext);
            }

            var session = sessionResult.Value;
            chatDb.ChatSessions.Add(session);
            await chatDb.SaveChangesAsync(cancellationToken);

            var sessionDto = session.ToDto();
            await hubContext.Clients.Group(ChatHubGroups.TenantStaff(tenantId)).ChatSessionClaimed(sessionDto);

            return Results.Created($"/api/v1/chat/sessions/{session.Id}", sessionDto);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<CreateChatSessionRequest>("application/json")
        .Produces<ChatSessionDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("StartChatSession");

        // 2. GET /api/v1/chat/sessions (Agent/Admin sees queue; Customer sees only own)
        group.MapGet("/sessions", async (
            [FromQuery] ChatSessionStatus? status,
            ChatDbContext chatDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isStaff, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "CHAT_UNAUTHORIZED",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var query = chatDb.ChatSessions
                .AsNoTracking()
                .Include(s => s.Messages)
                .Where(s => s.TenantId == tenantId);

            if (!isStaff)
            {
                query = query.Where(s => s.CustomerId == callerUserId);
            }

            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            var sessions = await query
                .OrderByDescending(s => s.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var result = sessions.Select(s => s.ToSummaryDto()).ToList();
            return Results.Ok(result);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<IReadOnlyList<ChatSessionSummaryDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetChatSessions");

        // 3. GET /api/v1/chat/sessions/{id} (Owning Customer or Tenant Agent/Admin)
        group.MapGet("/sessions/{id:guid}", async (
            Guid id,
            ChatDbContext chatDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isStaff, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "CHAT_UNAUTHORIZED",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var session = await chatDb.ChatSessions
                .AsNoTracking()
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, cancellationToken);

            if (session == null || (!isStaff && session.CustomerId != callerUserId))
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Chat session '{id}' was not found.",
                    "ChatSession.NotFound",
                    httpContext);
            }

            return Results.Ok(session.ToDto());
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<ChatSessionDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("GetChatSessionById");

        // 4. POST /api/v1/chat/sessions/{id}/claim (Agent/Admin claims a waiting session)
        group.MapPost("/sessions/{id:guid}/claim", async (
            Guid id,
            ChatDbContext chatDb,
            ITenantContextAccessor tenantContextAccessor,
            IHubContext<ChatHub, IChatHubClient> hubContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isStaff, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "CHAT_UNAUTHORIZED",
                    httpContext);
            }

            if (!isStaff)
            {
                return CreateProblemResult(
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Only support agents and administrators can claim chat sessions.",
                    "CHAT_CLAIM_FORBIDDEN",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var session = await chatDb.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, cancellationToken);

            if (session == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Chat session '{id}' was not found.",
                    "ChatSession.NotFound",
                    httpContext);
            }

            var claimResult = session.Claim(callerUserId);
            if (claimResult.IsFailure)
            {
                return MapErrorToProblemResult(claimResult.Error, httpContext);
            }

            await chatDb.SaveChangesAsync(cancellationToken);

            var sessionDto = session.ToDto();
            await hubContext.Clients.Group(ChatHubGroups.Session(tenantId, session.Id)).ChatSessionClaimed(sessionDto);
            await hubContext.Clients.Group(ChatHubGroups.TenantStaff(tenantId)).ChatSessionClaimed(sessionDto);

            return Results.Ok(sessionDto);
        })
        .RequireAuthorization(IdentityPolicies.AgentOrHigher)
        .Produces<ChatSessionDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("ClaimChatSession");

        // 5. POST /api/v1/chat/sessions/{id}/messages (Owning Customer or Agent/Admin appends message)
        group.MapPost("/sessions/{id:guid}/messages", async (
            Guid id,
            [FromBody] SendChatMessageRequest request,
            ChatDbContext chatDb,
            ITenantContextAccessor tenantContextAccessor,
            IHubContext<ChatHub, IChatHubClient> hubContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isStaff, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "CHAT_UNAUTHORIZED",
                    httpContext);
            }

            var content = request.MessageText;
            if (string.IsNullOrWhiteSpace(content))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Message content cannot be empty.",
                    "CHAT_MESSAGE_EMPTY",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var session = await chatDb.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, cancellationToken);

            if (session == null || (!isStaff && session.CustomerId != callerUserId))
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Chat session '{id}' was not found.",
                    "ChatSession.NotFound",
                    httpContext);
            }

            var senderRole = isStaff ? "Agent" : "Customer";
            var addResult = session.AddMessage(callerUserId, senderRole, content);
            if (addResult.IsFailure)
            {
                return MapErrorToProblemResult(addResult.Error, httpContext);
            }

            chatDb.ChatMessages.Add(addResult.Value);
            await chatDb.SaveChangesAsync(cancellationToken);

            var messageDto = addResult.Value.ToDto();
            await hubContext.Clients.Group(ChatHubGroups.Session(tenantId, session.Id)).ChatMessageReceived(messageDto);

            return Results.Created($"/api/v1/chat/sessions/{session.Id}/messages/{messageDto.Id}", messageDto);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<SendChatMessageRequest>("application/json")
        .Produces<ChatMessageDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("SendChatMessage");

        // 6. POST /api/v1/chat/sessions/{id}/close (Owning Customer or Agent/Admin closes session)
        group.MapPost("/sessions/{id:guid}/close", async (
            Guid id,
            [FromBody] CloseChatSessionRequest? request,
            ChatDbContext chatDb,
            ITenantContextAccessor tenantContextAccessor,
            IHubContext<ChatHub, IChatHubClient> hubContext,
            ITicketCreationService? ticketCreationService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isStaff, out _))
            {
                return CreateProblemResult(
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    "Authenticated user identity claim is missing or invalid.",
                    "CHAT_UNAUTHORIZED",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var session = await chatDb.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, cancellationToken);

            if (session == null || (!isStaff && session.CustomerId != callerUserId))
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Chat session '{id}' was not found.",
                    "ChatSession.NotFound",
                    httpContext);
            }

            var convertToTicket = request?.ConvertToTicket ?? false;
            if (convertToTicket)
            {
                if (ticketCreationService == null)
                {
                    return CreateProblemResult(
                        StatusCodes.Status500InternalServerError,
                        "Service Unavailable",
                        "Ticket creation service is not available.",
                        "TICKET_SERVICE_UNAVAILABLE",
                        httpContext);
                }

                // Format chat transcript into ticket description
                var transcript = string.Join("\n", session.Messages.OrderBy(m => m.SentAtUtc).Select(m => $"[{m.SentAtUtc:u}] {m.SenderRole}: {m.Content}"));
                var description = string.IsNullOrWhiteSpace(transcript)
                    ? $"Follow-up ticket from Live Chat session: {session.Subject}"
                    : $"Live Chat Transcript:\n{transcript}";

                var createTicketReq = new CreateTicketFromChatRequest(
                    tenantId,
                    session.CustomerId,
                    session.Subject,
                    description,
                    callerUserId);

                var ticketResult = await ticketCreationService.CreateTicketFromChatAsync(createTicketReq, cancellationToken);
                if (ticketResult.IsFailure)
                {
                    return MapErrorToProblemResult(ticketResult.Error, httpContext);
                }

                var convertResult = session.ConvertToTicket(ticketResult.Value);
                if (convertResult.IsFailure)
                {
                    return MapErrorToProblemResult(convertResult.Error, httpContext);
                }
            }
            else
            {
                var closeResult = session.Close();
                if (closeResult.IsFailure)
                {
                    return MapErrorToProblemResult(closeResult.Error, httpContext);
                }
            }

            await chatDb.SaveChangesAsync(cancellationToken);

            var closedDto = new ChatSessionClosedDto(
                session.Id,
                tenantId,
                session.Status,
                session.ConvertedTicketId,
                session.ClosedAtUtc!.Value);

            await hubContext.Clients.Group(ChatHubGroups.Session(tenantId, session.Id)).ChatSessionClosed(closedDto);

            return Results.Ok(session.ToDto());
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Accepts<CloseChatSessionRequest>("application/json")
        .Produces<ChatSessionDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("CloseChatSession");

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
                "A valid tenant identifier (X-Tenant-Subdomain, X-Tenant-Id, or Host subdomain) is required for chat endpoints.",
                "TENANT_NOT_RESOLVED",
                httpContext);
            return false;
        }

        errorResult = null;
        return true;
    }

    private static bool TryGetCallerIdentity(
        ClaimsPrincipal user,
        out Guid userId,
        out bool isStaff,
        out string role)
    {
        userId = Guid.Empty;
        isStaff = false;
        role = string.Empty;

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out userId) || userId == Guid.Empty)
        {
            return false;
        }

        role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role") ?? string.Empty;
        isStaff = string.Equals(role, IdentityRoles.Agent, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(role, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        return true;
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
