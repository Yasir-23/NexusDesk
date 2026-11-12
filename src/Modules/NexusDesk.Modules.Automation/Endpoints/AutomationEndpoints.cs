using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Automation.Application;
using NexusDesk.Modules.Automation.Application.Contracts;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Endpoints;

public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tenant-admin")
            .WithTags("TenantAdmin - SLA & Automation")
            .RequireAuthorization(IdentityPolicies.TenantAdminOrHigher);

        // 1. PUT /api/v1/tenant-admin/sla-policies — Upsert SLA policy for a priority
        group.MapPut("/sla-policies", async (
            [FromBody] UpsertSlaPolicyRequest request,
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            if (!SlaPolicy.TryNormalizePriority(request.Priority, out var normalizedPriority))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Priority must be one of: Low, Medium, High, Urgent.",
                    "SlaPolicy.InvalidPriority",
                    httpContext);
            }

            var existing = await automationDb.SlaPolicies
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Priority == normalizedPriority, cancellationToken);

            if (existing != null)
            {
                var updateResult = existing.UpdateTargets(
                    request.FirstResponseTargetMinutes,
                    request.ResolutionTargetMinutes);
                if (updateResult.IsFailure)
                {
                    return MapErrorToProblemResult(updateResult.Error, httpContext);
                }

                await automationDb.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(existing));
            }

            var createResult = SlaPolicy.Create(
                tenantId,
                normalizedPriority,
                request.FirstResponseTargetMinutes,
                request.ResolutionTargetMinutes);

            if (createResult.IsFailure)
            {
                return MapErrorToProblemResult(createResult.Error, httpContext);
            }

            automationDb.SlaPolicies.Add(createResult.Value);
            await automationDb.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToResponse(createResult.Value));
        })
        .Accepts<UpsertSlaPolicyRequest>("application/json")
        .Produces<SlaPolicyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("UpsertSlaPolicy");

        // 2. GET /api/v1/tenant-admin/sla-policies — List tenant SLA policies
        group.MapGet("/sla-policies", async (
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var policies = await automationDb.SlaPolicies
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .OrderBy(p => p.Priority)
                .Select(p => ToResponse(p))
                .ToListAsync(cancellationToken);

            return Results.Ok(policies);
        })
        .Produces<IReadOnlyList<SlaPolicyResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetSlaPolicies");

        // 3. POST /api/v1/tenant-admin/automation-rules — Create automation rule
        group.MapPost("/automation-rules", async (
            [FromBody] CreateAutomationRuleRequest request,
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var createResult = AutomationRule.Create(
                tenantId,
                request.Name,
                request.TriggerEvent,
                request.ConditionJson,
                request.ActionType,
                request.ActionPayloadJson,
                request.IsEnabled);

            if (createResult.IsFailure)
            {
                return MapErrorToProblemResult(createResult.Error, httpContext);
            }

            automationDb.AutomationRules.Add(createResult.Value);
            await automationDb.SaveChangesAsync(cancellationToken);

            var response = ToResponse(createResult.Value);
            return Results.Created($"/api/v1/tenant-admin/automation-rules/{response.Id}", response);
        })
        .Accepts<CreateAutomationRuleRequest>("application/json")
        .Produces<AutomationRuleResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("CreateAutomationRule");

        // 4. GET /api/v1/tenant-admin/automation-rules — List tenant automation rules
        group.MapGet("/automation-rules", async (
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var rules = await automationDb.AutomationRules
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => ToResponse(r))
                .ToListAsync(cancellationToken);

            return Results.Ok(rules);
        })
        .Produces<IReadOnlyList<AutomationRuleResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetAutomationRules");

        // 5. PATCH /api/v1/tenant-admin/automation-rules/{id}/toggle — Enable/disable rule
        group.MapPatch("/automation-rules/{id:guid}/toggle", async (
            Guid id,
            [FromBody] ToggleAutomationRuleRequest? request,
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var rule = await automationDb.AutomationRules
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken);

            if (rule == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Resource Not Found",
                    $"Automation rule '{id}' was not found.",
                    "AutomationRule.NotFound",
                    httpContext);
            }

            if (request?.IsEnabled.HasValue == true)
            {
                rule.SetEnabled(request.IsEnabled.Value);
            }
            else
            {
                rule.Toggle();
            }

            await automationDb.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(rule));
        })
        .Produces<AutomationRuleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("ToggleAutomationRule");

        // 5. POST /api/v1/tenant-admin/webhooks — Register external webhook receiver URL and secret
        group.MapPost("/webhooks", async (
            [FromBody] CreateWebhookSubscriptionRequest request,
            AutomationDbContext automationDb,
            IDataProtectionProvider dataProtectionProvider,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            if (string.IsNullOrWhiteSpace(request.TargetUrl))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Target URL is required.",
                    "Webhook.InvalidTargetUrl",
                    httpContext);
            }

            if (!Uri.TryCreate(request.TargetUrl, UriKind.Absolute, out var uri))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Target URL must be a valid absolute URI.",
                    "Webhook.InvalidTargetUrl",
                    httpContext);
            }

            var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var isLocalHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                              (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

            if (!isHttps && !isLocalHttp)
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Target URL must use HTTPS (HTTP is only permitted for localhost during testing).",
                    "Webhook.InvalidTargetUrlScheme",
                    httpContext);
            }

            if (string.IsNullOrWhiteSpace(request.Secret) || request.Secret.Trim().Length < 16)
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Webhook secret must be at least 16 characters.",
                    "Webhook.SecretTooShort",
                    httpContext);
            }

            var eventTypes = request.EventTypes?.ToList() ?? [];
            if (eventTypes.Count == 0)
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "At least one event type must be specified.",
                    "Webhook.EmptyEventTypes",
                    httpContext);
            }

            foreach (var evt in eventTypes)
            {
                var trimmed = evt?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(trimmed) || !WebhookSubscription.ValidEventTypes.Contains(trimmed))
                {
                    return CreateProblemResult(
                        StatusCodes.Status400BadRequest,
                        "Validation Failed",
                        $"Event type '{evt}' is invalid. Supported event types: {string.Join(", ", WebhookSubscription.ValidEventTypes)}.",
                        "Webhook.InvalidEventType",
                        httpContext);
                }
            }

            var protector = dataProtectionProvider.CreateProtector(WebhookSigner.DataProtectionPurpose);
            var encryptedSecret = protector.Protect(request.Secret.Trim());

            var createResult = WebhookSubscription.Create(
                tenantId,
                uri.ToString(),
                encryptedSecret,
                eventTypes,
                request.IsActive);

            if (createResult.IsFailure)
            {
                return MapErrorToProblemResult(createResult.Error, httpContext);
            }

            automationDb.WebhookSubscriptions.Add(createResult.Value);
            await automationDb.SaveChangesAsync(cancellationToken);

            var response = ToResponse(createResult.Value);
            return Results.Created($"/api/v1/tenant-admin/webhooks/{createResult.Value.Id}", response);
        })
        .Accepts<CreateWebhookSubscriptionRequest>("application/json")
        .Produces<WebhookSubscriptionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("CreateWebhookSubscription");

        // 6. GET /api/v1/tenant-admin/webhooks — List active tenant's webhook subscriptions
        group.MapGet("/webhooks", async (
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var subscriptions = await automationDb.WebhookSubscriptions
                .AsNoTracking()
                .Where(w => w.TenantId == tenantId)
                .OrderByDescending(w => w.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            return Results.Ok(subscriptions.Select(ToResponse).ToList());
        })
        .Produces<List<WebhookSubscriptionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("ListWebhookSubscriptions");

        // 7. DELETE /api/v1/tenant-admin/webhooks/{id} — Delete webhook subscription
        group.MapDelete("/webhooks/{id:guid}", async (
            [FromRoute] Guid id,
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var subscription = await automationDb.WebhookSubscriptions
                .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId, cancellationToken);

            if (subscription == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Not Found",
                    $"Webhook subscription '{id}' was not found.",
                    "Webhook.NotFound",
                    httpContext);
            }

            automationDb.WebhookSubscriptions.Remove(subscription);
            await automationDb.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("DeleteWebhookSubscription");

        // 8. PATCH /api/v1/tenant-admin/webhooks/{id}/toggle — Toggle active state
        group.MapPatch("/webhooks/{id:guid}/toggle", async (
            [FromRoute] Guid id,
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var subscription = await automationDb.WebhookSubscriptions
                .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId, cancellationToken);

            if (subscription == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Not Found",
                    $"Webhook subscription '{id}' was not found.",
                    "Webhook.NotFound",
                    httpContext);
            }

            subscription.Toggle();
            await automationDb.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToResponse(subscription));
        })
        .Produces<WebhookSubscriptionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("ToggleWebhookSubscription");

        // 9. GET /api/v1/tenant-admin/webhooks/outbox — Diagnostic read endpoint for recent outbox entries
        group.MapGet("/webhooks/outbox", async (
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var messages = await automationDb.OutboxMessages
                .AsNoTracking()
                .Where(m => m.TenantId == tenantId)
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(100)
                .ToListAsync(cancellationToken);

            return Results.Ok(messages.Select(ToResponse).ToList());
        })
        .Produces<List<OutboxMessageResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetOutboxMessages");

        // 10. GET /api/v1/tenant-admin/webhooks/dead-letters — Diagnostic read endpoint for dead letters
        group.MapGet("/webhooks/dead-letters", async (
            AutomationDbContext automationDb,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantId, out var tenantError))
            {
                return tenantError!;
            }

            var deadLetters = await automationDb.DeadLetterWebhooks
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId)
                .OrderByDescending(d => d.FailedAtUtc)
                .Take(100)
                .ToListAsync(cancellationToken);

            return Results.Ok(deadLetters.Select(ToResponse).ToList());
        })
        .Produces<List<DeadLetterWebhookResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .WithName("GetDeadLetterWebhooks");

        return app;
    }

    private static SlaPolicyResponse ToResponse(SlaPolicy policy) =>
        new(
            policy.Id,
            policy.TenantId,
            policy.Priority,
            policy.FirstResponseTargetMinutes,
            policy.ResolutionTargetMinutes,
            policy.CreatedAtUtc,
            policy.UpdatedAtUtc);

    private static AutomationRuleResponse ToResponse(AutomationRule rule) =>
        new(
            rule.Id,
            rule.TenantId,
            rule.Name,
            rule.TriggerEvent,
            rule.ConditionJson,
            rule.ActionType,
            rule.ActionPayloadJson,
            rule.IsEnabled,
            rule.CreatedAtUtc,
            rule.UpdatedAtUtc);

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription subscription) =>
        new(
            subscription.Id,
            subscription.TenantId,
            subscription.TargetUrl,
            subscription.EventTypes,
            subscription.IsActive,
            subscription.CreatedAtUtc,
            subscription.UpdatedAtUtc);

    private static OutboxMessageResponse ToResponse(OutboxMessage message) =>
        new(
            message.Id,
            message.TenantId,
            message.EventType,
            message.PayloadJson,
            message.Status.ToString(),
            message.RetryCount,
            message.NextRetryAtUtc,
            message.LastError,
            message.CreatedAtUtc,
            message.ProcessedAtUtc);

    private static DeadLetterWebhookResponse ToResponse(DeadLetterWebhook deadLetter) =>
        new(
            deadLetter.Id,
            deadLetter.TenantId,
            deadLetter.OutboxMessageId,
            deadLetter.WebhookSubscriptionId,
            deadLetter.TargetUrl,
            deadLetter.EventType,
            deadLetter.PayloadJson,
            deadLetter.AttemptCount,
            deadLetter.LastHttpStatusCode,
            deadLetter.LastError,
            deadLetter.FailedAtUtc);

    private static bool TryEnsureTenantResolved(
        ITenantContextAccessor accessor,
        HttpContext httpContext,
        out Guid tenantId,
        out IResult? errorResult)
    {
        var tenantContext = accessor.TenantContext;
        if (tenantContext == null || !tenantContext.IsResolved || !tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            tenantId = Guid.Empty;
            errorResult = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                "Tenant Not Resolved",
                "A valid tenant identifier (X-Tenant-Subdomain, X-Tenant-Id, or Host subdomain) is required.",
                "TENANT_NOT_RESOLVED",
                httpContext);
            return false;
        }

        tenantId = tenantContext.TenantId.Value;
        errorResult = null;
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
