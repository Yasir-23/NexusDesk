using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
