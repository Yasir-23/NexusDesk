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
using NexusDesk.Modules.Ticketing.Domain.Entities;
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

        // 9. POST /api/v1/tickets/{id}/attachments — Upload file attachment
        group.MapPost("/{id:guid}/attachments", async (
            Guid id,
            IFormFile file,
            TicketingDbContext ticketingDb,
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
                return CreateProblemResult(StatusCodes.Status401Unauthorized, "Unauthorized", "Valid user identity required.", "UNAUTHORIZED", httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticket = await ticketingDb.Tickets
                .AsNoTracking()
                .Where(t => t.Id == id && t.TenantId == tenantId)
                .Select(t => new { t.Id, t.CustomerId })
                .FirstOrDefaultAsync(cancellationToken);

            if (ticket == null)
            {
                return CreateProblemResult(StatusCodes.Status404NotFound, "Ticket Not Found", $"Ticket '{id}' was not found.", "Ticket.NotFound", httpContext);
            }

            if (isCustomer && ticket.CustomerId != callerUserId)
            {
                return CreateProblemResult(StatusCodes.Status403Forbidden, "Forbidden", "You do not have access to this ticket.", "FORBIDDEN", httpContext);
            }

            var uploadedFile = file;
            if ((uploadedFile == null || uploadedFile.Length == 0) && httpContext.Request.HasFormContentType && httpContext.Request.Form.Files.Count > 0)
            {
                uploadedFile = httpContext.Request.Form.Files[0];
            }

            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                return CreateProblemResult(StatusCodes.Status400BadRequest, "No File Uploaded", "A file must be provided via multipart/form-data.", "NO_FILE_PROVIDED", httpContext);
            }

            const long maxSizeBytes = 10 * 1024 * 1024; // 10 MB
            if (uploadedFile.Length > maxSizeBytes)
            {
                return CreateProblemResult(StatusCodes.Status400BadRequest, "File Too Large", "Attachments cannot exceed 10 MB in size.", "FILE_TOO_LARGE", httpContext);
            }

            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".pdf", ".txt", ".log", ".csv", ".zip"
            };

            if (!allowedExtensions.Contains(ext))
            {
                return CreateProblemResult(StatusCodes.Status400BadRequest, "Invalid Extension", $"File extension '{ext}' is not permitted.", "INVALID_EXTENSION", httpContext);
            }

            using var stream = uploadedFile.OpenReadStream();
            var header = new byte[16];
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            stream.Position = 0;

            var isSignatureValid = ext switch
            {
                ".png" => bytesRead >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                ".jpg" or ".jpeg" => bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".pdf" => bytesRead >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
                ".zip" => bytesRead >= 2 && header[0] == 0x50 && header[1] == 0x4B,
                ".txt" or ".log" or ".csv" => !header.Take(bytesRead).Any(b => b == 0),
                _ => false
            };

            if (!isSignatureValid)
            {
                return CreateProblemResult(StatusCodes.Status400BadRequest, "Invalid Magic Bytes", "The file content does not match its declared extension signature.", "INVALID_MAGIC_BYTES", httpContext);
            }

            var storageDir = Path.Combine(AppContext.BaseDirectory, "App_Data", "attachments", tenantId.ToString("D"), id.ToString("D"));
            Directory.CreateDirectory(storageDir);

            var attachmentId = Guid.NewGuid();
            var safeFileName = Path.GetFileName(uploadedFile.FileName);
            var storagePath = Path.Combine(storageDir, $"{attachmentId:N}_{safeFileName}");

            await using (var fileStream = new FileStream(storagePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var attachment = new TicketAttachment(
                attachmentId,
                tenantId,
                id,
                callerUserId,
                safeFileName,
                string.IsNullOrWhiteSpace(uploadedFile.ContentType) ? "application/octet-stream" : uploadedFile.ContentType,
                uploadedFile.Length,
                storagePath,
                now);

            ticketingDb.TicketAttachments.Add(attachment);
            await ticketingDb.SaveChangesAsync(cancellationToken);

            var dto = new TicketAttachmentDto(
                attachment.Id,
                attachment.TicketId,
                attachment.UploadedByUserId,
                attachment.FileName,
                attachment.ContentType,
                attachment.SizeBytes,
                attachment.CreatedAtUtc);

            return Results.Created($"/api/v1/tickets/{id}/attachments/{attachment.Id}", dto);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .DisableAntiforgery()
        .Produces<TicketAttachmentDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("UploadTicketAttachment");

        // 10. GET /api/v1/tickets/{id}/attachments — List attachments for a ticket
        group.MapGet("/{id:guid}/attachments", async (
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

            if (!TryGetCallerIdentity(httpContext.User, out var callerUserId, out var isCustomer))
            {
                return CreateProblemResult(StatusCodes.Status401Unauthorized, "Unauthorized", "Valid user identity required.", "UNAUTHORIZED", httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticket = await ticketingDb.Tickets
                .AsNoTracking()
                .Where(t => t.Id == id && t.TenantId == tenantId)
                .Select(t => new { t.Id, t.CustomerId })
                .FirstOrDefaultAsync(cancellationToken);

            if (ticket == null)
            {
                return CreateProblemResult(StatusCodes.Status404NotFound, "Ticket Not Found", $"Ticket '{id}' was not found.", "Ticket.NotFound", httpContext);
            }

            if (isCustomer && ticket.CustomerId != callerUserId)
            {
                return CreateProblemResult(StatusCodes.Status403Forbidden, "Forbidden", "You do not have access to this ticket.", "FORBIDDEN", httpContext);
            }

            var attachments = await ticketingDb.TicketAttachments
                .AsNoTracking()
                .Where(a => a.TicketId == id && a.TenantId == tenantId)
                .OrderBy(a => a.CreatedAtUtc)
                .Select(a => new TicketAttachmentDto(
                    a.Id,
                    a.TicketId,
                    a.UploadedByUserId,
                    a.FileName,
                    a.ContentType,
                    a.SizeBytes,
                    a.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(attachments);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces<List<TicketAttachmentDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("ListTicketAttachments");

        // 11. GET /api/v1/tickets/{id}/attachments/{attachmentId}/download — Stream attachment
        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}/download", async (
            Guid id,
            Guid attachmentId,
            TicketingDbContext ticketingDb,
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
                return CreateProblemResult(StatusCodes.Status401Unauthorized, "Unauthorized", "Valid user identity required.", "UNAUTHORIZED", httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticket = await ticketingDb.Tickets
                .AsNoTracking()
                .Where(t => t.Id == id && t.TenantId == tenantId)
                .Select(t => new { t.Id, t.CustomerId })
                .FirstOrDefaultAsync(cancellationToken);

            if (ticket == null)
            {
                return CreateProblemResult(StatusCodes.Status404NotFound, "Ticket Not Found", $"Ticket '{id}' was not found.", "Ticket.NotFound", httpContext);
            }

            if (isCustomer && ticket.CustomerId != callerUserId)
            {
                return CreateProblemResult(StatusCodes.Status403Forbidden, "Forbidden", "You do not have access to this ticket.", "FORBIDDEN", httpContext);
            }

            var attachment = await ticketingDb.TicketAttachments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TicketId == id && a.TenantId == tenantId, cancellationToken);

            if (attachment == null)
            {
                return CreateProblemResult(StatusCodes.Status404NotFound, "Attachment Not Found", $"Attachment '{attachmentId}' was not found.", "Attachment.NotFound", httpContext);
            }

            if (!File.Exists(attachment.StoragePath))
            {
                return CreateProblemResult(StatusCodes.Status404NotFound, "File Missing", "The physical attachment file was not found on disk.", "FILE_NOT_FOUND", httpContext);
            }

            return Results.File(
                attachment.StoragePath,
                contentType: attachment.ContentType,
                fileDownloadName: attachment.FileName,
                enableRangeProcessing: true);
        })
        .RequireAuthorization(IdentityPolicies.AuthenticatedTenantUser)
        .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("DownloadTicketAttachment");

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
