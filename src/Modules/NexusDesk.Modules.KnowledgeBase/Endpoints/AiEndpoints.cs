using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.KnowledgeBase.Domain;
using NexusDesk.Modules.KnowledgeBase.Persistence;
using NexusDesk.Modules.KnowledgeBase.Services;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Ticketing;

namespace NexusDesk.Modules.KnowledgeBase.Endpoints;

public sealed record SuggestReplyRequest(string? CustomInstructions = null);

public sealed record ReferencedArticleDto(Guid Id, string Title, string Slug);

public sealed record SuggestReplyResponse(
    Guid TicketId,
    string SuggestedReply,
    IReadOnlyList<ReferencedArticleDto> ReferencedArticles);

public sealed record SummarizeTicketRequest(string? CustomInstructions = null);

public sealed record SummarizeTicketResponse(
    Guid TicketId,
    string Summary,
    IReadOnlyList<string> KeyPoints,
    int CommentCount);

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ai")
            .WithTags("AI Ticket Assist")
            .RequireAuthorization(IdentityPolicies.AgentOrHigher);

        // 1. POST /api/v1/ai/tickets/{ticketId}/suggest-reply
        group.MapPost("/tickets/{ticketId:guid}/suggest-reply", async (
            Guid ticketId,
            [FromBody] SuggestReplyRequest? request,
            IPromptInjectionGuard promptGuard,
            ITicketDataProvider ticketDataProvider,
            KnowledgeBaseDbContext kbDb,
            ITenantContextAccessor tenantContextAccessor,
            IConfiguration configuration,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!promptGuard.TryValidate(request?.CustomInstructions, out var detectedPattern))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Prompt Injection Detected",
                    $"Adversarial prompt injection pattern detected: '{detectedPattern}'.",
                    "PROMPT_INJECTION_DETECTED",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticket = await ticketDataProvider.GetTicketAiContextAsync(tenantId, ticketId, cancellationToken);
            if (ticket == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Ticket Not Found",
                    $"Ticket '{ticketId}' was not found.",
                    "Ticket.NotFound",
                    httpContext);
            }

            // Find published articles in tenant matching ticket terms or most recent
            var articles = await kbDb.KbArticles
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Status == KbArticleStatus.Published)
                .OrderByDescending(a => a.UpdatedAtUtc)
                .Take(3)
                .Select(a => new ReferencedArticleDto(a.Id, a.Title, a.Slug))
                .ToListAsync(cancellationToken);

            var suggestedReply = articles.Count > 0
                ? $"Hello,\n\nThank you for reaching out regarding '{ticket.Title}'. Based on our knowledge base, please refer to the following guide: '{articles[0].Title}'. If you need further assistance, please let us know.\n\nBest regards,\nSupport Team"
                : $"Hello,\n\nThank you for reaching out regarding '{ticket.Title}'. We have received your inquiry and our team is investigating. We will update you as soon as more details are available.\n\nBest regards,\nSupport Team";

            return Results.Ok(new SuggestReplyResponse(
                ticketId,
                suggestedReply,
                articles));
        })
        .WithName("SuggestTicketReply")
        .Produces<SuggestReplyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // 2. POST /api/v1/ai/tickets/{ticketId}/summarize
        group.MapPost("/tickets/{ticketId:guid}/summarize", async (
            Guid ticketId,
            [FromBody] SummarizeTicketRequest? request,
            IPromptInjectionGuard promptGuard,
            ITicketDataProvider ticketDataProvider,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!promptGuard.TryValidate(request?.CustomInstructions, out var detectedPattern))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Prompt Injection Detected",
                    $"Adversarial prompt injection pattern detected: '{detectedPattern}'.",
                    "PROMPT_INJECTION_DETECTED",
                    httpContext);
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var ticket = await ticketDataProvider.GetTicketAiContextAsync(tenantId, ticketId, cancellationToken);
            if (ticket == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Ticket Not Found",
                    $"Ticket '{ticketId}' was not found.",
                    "Ticket.NotFound",
                    httpContext);
            }

            var summary = $"Ticket {ticket.TicketNumber} ('{ticket.Title}'): {ticket.Description.Trim()}";
            if (ticket.Comments.Count > 0)
            {
                summary += $" Thread includes {ticket.Comments.Count} discussion comment(s).";
            }

            var keyPoints = new List<string>
            {
                $"Subject: {ticket.Title}",
                $"Initial Report: {(ticket.Description.Length > 140 ? ticket.Description[..140] + "..." : ticket.Description)}"
            };

            if (ticket.Comments.Count > 0)
            {
                keyPoints.Add($"Latest activity: {(ticket.Comments[^1].Length > 140 ? ticket.Comments[^1][..140] + "..." : ticket.Comments[^1])}");
            }

            return Results.Ok(new SummarizeTicketResponse(
                ticketId,
                summary,
                keyPoints,
                ticket.Comments.Count));
        })
        .WithName("SummarizeTicket")
        .Produces<SummarizeTicketResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);

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
                "A valid tenant identifier (X-Tenant-Subdomain, X-Tenant-Id, or Host subdomain) is required for AI endpoints.",
                "TENANT_NOT_RESOLVED",
                httpContext);
            return false;
        }

        errorResult = null;
        return true;
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
