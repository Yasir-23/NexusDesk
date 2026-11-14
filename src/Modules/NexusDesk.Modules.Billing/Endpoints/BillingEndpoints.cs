using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NexusDesk.Modules.Billing.Contracts;
using NexusDesk.Modules.Billing.Domain;
using NexusDesk.Modules.Billing.Persistence;
using NexusDesk.Modules.Billing.Services;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Billing.Endpoints;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/billing")
            .WithTags("Billing");

        // 1. Public Plan Catalog
        group.MapGet("/plans", async (BillingDbContext dbContext, CancellationToken ct) =>
        {
            var plans = await dbContext.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.MonthlyPriceCents)
                .Select(p => new SubscriptionPlanDto(
                    p.Id,
                    p.Code,
                    p.Name,
                    p.MonthlyPriceCents,
                    p.MaxAgents,
                    p.MaxMonthlyTickets,
                    p.MaxMonthlyAiTokens,
                    p.CustomBrandingEnabled,
                    p.WebhooksEnabled,
                    p.SlaEnabled,
                    p.IsActive))
                .ToListAsync(ct);

            return Results.Ok(plans);
        })
        .AllowAnonymous()
        .WithName("GetBillingPlans")
        .WithSummary("List all active subscription plans");

        // 2. Tenant Subscription Management
        group.MapGet("/subscription", async (
            BillingDbContext dbContext,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var tenantId = tenantAccessor.TenantContext.TenantId;
            var sub = await EnsureTenantSubscriptionAsync(dbContext, tenantAccessor, ct);
            var plan = await dbContext.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == sub.PlanCode, ct)
                ?? await dbContext.SubscriptionPlans.FirstAsync(p => p.Code == "Free", ct);

            var periodKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");
            var usageRecords = await dbContext.UsageMeterRecords
                .Where(u => u.PeriodKey == periodKey)
                .ToListAsync(ct);

            var metrics = BuildMetricUsageList(plan, usageRecords);

            var planDto = new SubscriptionPlanDto(
                plan.Id,
                plan.Code,
                plan.Name,
                plan.MonthlyPriceCents,
                plan.MaxAgents,
                plan.MaxMonthlyTickets,
                plan.MaxMonthlyAiTokens,
                plan.CustomBrandingEnabled,
                plan.WebhooksEnabled,
                plan.SlaEnabled,
                plan.IsActive);

            var subDto = new TenantSubscriptionDto(
                sub.Id,
                sub.TenantId,
                sub.PlanCode,
                sub.Status,
                sub.BillingCycle,
                sub.StripeCustomerId,
                sub.StripeSubscriptionId,
                sub.CurrentPeriodStartUtc,
                sub.CurrentPeriodEndUtc,
                sub.CancelAtPeriodEnd,
                sub.UpdatedAtUtc);

            return Results.Ok(new TenantSubscriptionDetailsDto(subDto, planDto, periodKey, metrics));
        })
        .RequireAuthorization("billing:read")
        .WithName("GetTenantSubscription")
        .WithSummary("Get current tenant subscription details and usage summary");

        // 3. Change Subscription Plan (Upgrade / Downgrade)
        group.MapPost("/subscription/change-plan", async (
            [FromBody] ChangePlanRequest request,
            BillingDbContext dbContext,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.PlanCode))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "planCode is required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var targetPlan = await dbContext.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == request.PlanCode && p.IsActive, ct);

            if (targetPlan == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Plan Not Found",
                    Detail = $"Subscription plan '{request.PlanCode}' does not exist or is inactive.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var sub = await EnsureTenantSubscriptionAsync(dbContext, tenantAccessor, ct);
            var periodKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");
            var currentUsage = await dbContext.UsageMeterRecords
                .Where(u => u.PeriodKey == periodKey)
                .ToListAsync(ct);

            var downgradeConflict = BillingQuotaService.ValidateDowngrade(targetPlan, currentUsage);
            if (downgradeConflict != null)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Type = "https://nexusdesk.io/errors/downgrade-conflict",
                    Title = "Plan Downgrade Conflict",
                    Status = StatusCodes.Status409Conflict,
                    Detail = downgradeConflict,
                    Instance = "/api/v1/billing/subscription/change-plan",
                    Extensions = { ["errorCode"] = "PLAN_DOWNGRADE_CONFLICT" }
                });
            }

            // Create invoice if upgrading to a paid plan
            if (targetPlan.MonthlyPriceCents > 0)
            {
                var now = DateTimeOffset.UtcNow;
                var invoice = new BillingInvoice(
                    Guid.NewGuid(),
                    sub.TenantId,
                    $"INV-{now:yyyyMM}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                    targetPlan.Code,
                    targetPlan.MonthlyPriceCents,
                    "usd",
                    "Paid",
                    now,
                    now.AddMonths(1),
                    paidAtUtc: now,
                    stripeInvoiceId: null,
                    hostedInvoiceUrl: null,
                    createdAtUtc: now);

                dbContext.BillingInvoices.Add(invoice);
            }

            sub.ChangePlan(targetPlan.Code, request.BillingCycle);
            await dbContext.SaveChangesAsync(ct);

            var subDto = new TenantSubscriptionDto(
                sub.Id,
                sub.TenantId,
                sub.PlanCode,
                sub.Status,
                sub.BillingCycle,
                sub.StripeCustomerId,
                sub.StripeSubscriptionId,
                sub.CurrentPeriodStartUtc,
                sub.CurrentPeriodEndUtc,
                sub.CancelAtPeriodEnd,
                sub.UpdatedAtUtc);

            return Results.Ok(subDto);
        })
        .RequireAuthorization("billing:manage")
        .WithName("ChangeSubscriptionPlan")
        .WithSummary("Upgrade or downgrade subscription plan with usage validation");

        // 4. Cancel Subscription
        group.MapPost("/subscription/cancel", async (
            [FromBody] CancelSubscriptionRequest? request,
            [FromQuery] bool? immediate,
            BillingDbContext dbContext,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var isImmediate = immediate ?? request?.Immediate ?? false;
            var sub = await EnsureTenantSubscriptionAsync(dbContext, tenantAccessor, ct);

            sub.Cancel(isImmediate);
            await dbContext.SaveChangesAsync(ct);

            var subDto = new TenantSubscriptionDto(
                sub.Id,
                sub.TenantId,
                sub.PlanCode,
                sub.Status,
                sub.BillingCycle,
                sub.StripeCustomerId,
                sub.StripeSubscriptionId,
                sub.CurrentPeriodStartUtc,
                sub.CurrentPeriodEndUtc,
                sub.CancelAtPeriodEnd,
                sub.UpdatedAtUtc);

            return Results.Ok(subDto);
        })
        .RequireAuthorization("billing:manage")
        .WithName("CancelSubscription")
        .WithSummary("Cancel subscription at period end or immediately");

        // 5. Query Usage for Billing Period
        group.MapGet("/usage", async (
            [FromQuery] string? period,
            BillingDbContext dbContext,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var sub = await EnsureTenantSubscriptionAsync(dbContext, tenantAccessor, ct);
            var plan = await dbContext.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == sub.PlanCode, ct)
                ?? await dbContext.SubscriptionPlans.FirstAsync(p => p.Code == "Free", ct);

            var targetPeriod = string.IsNullOrWhiteSpace(period)
                ? DateTimeOffset.UtcNow.ToString("yyyy-MM")
                : period.Trim();

            var records = await dbContext.UsageMeterRecords
                .Where(u => u.PeriodKey == targetPeriod)
                .ToListAsync(ct);

            var metrics = BuildMetricUsageList(plan, records);

            return Results.Ok(new UsagePeriodDto(targetPeriod, plan.Code, metrics));
        })
        .RequireAuthorization("billing:read")
        .WithName("GetBillingUsage")
        .WithSummary("Get usage metrics vs plan quotas for requested period");

        // 6. Record / Increment Usage
        group.MapPost("/usage/record", async (
            [FromBody] RecordUsageRequest request,
            BillingDbContext dbContext,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.MetricName) || request.Quantity <= 0)
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Request",
                    Detail = "metricName must be specified and quantity must be greater than zero.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var sub = await EnsureTenantSubscriptionAsync(dbContext, tenantAccessor, ct);
            var plan = await dbContext.SubscriptionPlans.FirstOrDefaultAsync(p => p.Code == sub.PlanCode, ct)
                ?? await dbContext.SubscriptionPlans.FirstAsync(p => p.Code == "Free", ct);

            var periodKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");
            var record = await dbContext.UsageMeterRecords
                .FirstOrDefaultAsync(u => u.MetricName == request.MetricName && u.PeriodKey == periodKey, ct);

            var current = record?.Quantity ?? 0L;
            var limit = BillingQuotaService.GetMetricLimit(plan, request.MetricName);

            if (limit != long.MaxValue && (current + request.Quantity) > limit)
            {
                return Results.Json(new ProblemDetails
                {
                    Type = "https://nexusdesk.io/errors/quota-exceeded",
                    Title = "Plan Quota Exceeded",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = $"Tenant on plan '{plan.Code}' has reached the maximum limit of {limit} for metric '{request.MetricName}'.",
                    Instance = "/api/v1/billing/usage/record",
                    Extensions =
                    {
                        ["errorCode"] = "PLAN_QUOTA_EXCEEDED",
                        ["metricName"] = request.MetricName,
                        ["limit"] = limit,
                        ["current"] = current,
                        ["requested"] = request.Quantity
                    }
                }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
            }

            if (record == null)
            {
                record = new UsageMeterRecord(
                    Guid.NewGuid(),
                    sub.TenantId,
                    request.MetricName,
                    periodKey,
                    request.Quantity,
                    DateTimeOffset.UtcNow);

                dbContext.UsageMeterRecords.Add(record);
            }
            else
            {
                record.Increment(request.Quantity);
            }

            await dbContext.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = record.Id,
                tenantId = record.TenantId,
                metricName = record.MetricName,
                periodKey = record.PeriodKey,
                quantity = record.Quantity,
                limit = limit == long.MaxValue ? -1 : limit,
                updatedAtUtc = record.UpdatedAtUtc
            });
        })
        .RequireAuthorization("billing:manage")
        .WithName("RecordBillingUsage")
        .WithSummary("Increment usage meter and enforce plan quota");

        // 7. Invoice History
        group.MapGet("/invoices", async (
            BillingDbContext dbContext,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var invoices = await dbContext.BillingInvoices
                .OrderByDescending(i => i.CreatedAtUtc)
                .Select(i => new BillingInvoiceDto(
                    i.Id,
                    i.TenantId,
                    i.InvoiceNumber,
                    i.StripeInvoiceId,
                    i.PlanCode,
                    i.AmountCents,
                    i.Currency,
                    i.Status,
                    i.PeriodStartUtc,
                    i.PeriodEndUtc,
                    i.PaidAtUtc,
                    i.HostedInvoiceUrl,
                    i.CreatedAtUtc))
                .ToListAsync(ct);

            return Results.Ok(invoices);
        })
        .RequireAuthorization("billing:read")
        .WithName("GetBillingInvoices")
        .WithSummary("List tenant billing invoices");

        // 8. Stripe Webhook Receiver
        group.MapPost("/webhooks/stripe", async (
            HttpContext httpContext,
            BillingDbContext dbContext,
            IStripeSignatureValidator signatureValidator,
            IConfiguration config,
            CancellationToken ct) =>
        {
            httpContext.Request.EnableBuffering();
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(ct);
            httpContext.Request.Body.Position = 0;

            var signatureHeader = httpContext.Request.Headers["Stripe-Signature"].ToString();
            var webhookSecret = config["Billing:StripeWebhookSecret"] ?? "whsec_test_secret_nexusdesk_dev_2026";

            if (!signatureValidator.ValidateSignature(signatureHeader, rawBody, webhookSecret))
            {
                return Results.Unauthorized();
            }

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var eventId = root.GetProperty("id").GetString();
            var eventType = root.GetProperty("type").GetString();

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Stripe Payload",
                    Detail = "Event id and type are required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Webhook Idempotency Check
            var processed = await dbContext.ProcessedStripeEvents.FindAsync([eventId], ct);
            if (processed != null)
            {
                return Results.Ok(new { received = true, eventId, duplicate = true });
            }

            await ProcessStripeEventAsync(dbContext, root, eventId, eventType, ct);

            return Results.Ok(new { received = true, eventId });
        })
        .AllowAnonymous()
        .WithName("StripeWebhook")
        .WithSummary("Handle Stripe billing lifecycle webhooks with HMAC-SHA256 signature verification");

        return endpoints;
    }

    private static async Task<TenantSubscription> EnsureTenantSubscriptionAsync(
        BillingDbContext dbContext,
        ITenantContextAccessor tenantAccessor,
        CancellationToken ct)
    {
        var tenantId = tenantAccessor.TenantContext?.TenantId ?? Guid.Empty;
        var sub = await dbContext.TenantSubscriptions.FirstOrDefaultAsync(ct);
        if (sub != null)
        {
            return sub;
        }

        var planTier = tenantAccessor.TenantContext?.PlanTier;
        var planCode = planTier switch
        {
            TenantPlanTier.Free => "Free",
            TenantPlanTier.Enterprise => "Enterprise",
            _ => (tenantAccessor.TenantContext?.Subdomain?.Contains("beta", StringComparison.OrdinalIgnoreCase) == true ? "Free" : "Pro")
        };

        var now = DateTimeOffset.UtcNow;
        sub = new TenantSubscription(
            Guid.NewGuid(),
            tenantId,
            planCode,
            "Active",
            "Monthly",
            now,
            now.AddDays(30),
            stripeCustomerId: null,
            stripeSubscriptionId: null,
            cancelAtPeriodEnd: false,
            updatedAtUtc: now);

        dbContext.TenantSubscriptions.Add(sub);
        await dbContext.SaveChangesAsync(ct);
        return sub;
    }

    private static IReadOnlyList<MetricUsageDto> BuildMetricUsageList(
        SubscriptionPlan plan,
        IReadOnlyList<UsageMeterRecord> usageRecords)
    {
        var standardMetrics = new[] { "tickets_created", "active_agents", "ai_tokens_used", "webhook_deliveries" };
        var list = new List<MetricUsageDto>();

        foreach (var metric in standardMetrics)
        {
            var record = usageRecords.FirstOrDefault(r => r.MetricName.Equals(metric, StringComparison.OrdinalIgnoreCase));
            var used = record?.Quantity ?? 0L;
            var limit = BillingQuotaService.GetMetricLimit(plan, metric);

            var remaining = limit == long.MaxValue ? -1L : Math.Max(0L, limit - used);
            var usagePct = limit == long.MaxValue || limit == 0 ? 0.0 : Math.Round((double)used / limit * 100.0, 2);
            var remainingPct = limit == long.MaxValue || limit == 0 ? 100.0 : Math.Max(0.0, 100.0 - usagePct);

            list.Add(new MetricUsageDto(
                metric,
                used,
                limit == long.MaxValue ? -1L : limit,
                remaining,
                usagePct,
                remainingPct));
        }

        return list;
    }

    [AllowCrossTenantQuery("Stripe webhooks resolve and update tenant subscriptions across tenants without HTTP tenant headers")]
    private static async Task ProcessStripeEventAsync(
        BillingDbContext dbContext,
        JsonElement root,
        string eventId,
        string eventType,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (root.TryGetProperty("data", out var data) &&
            data.TryGetProperty("object", out var obj))
        {
            var customerId = obj.TryGetProperty("customer", out var c) ? c.GetString() : null;
            var subscriptionId = obj.TryGetProperty("subscription", out var s) ? s.GetString() : null;
            string? metadataTenantId = null;

            if (obj.TryGetProperty("metadata", out var meta) &&
                meta.TryGetProperty("tenant_id", out var mt))
            {
                metadataTenantId = mt.GetString();
            }

            TenantSubscription? sub = null;
            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                sub = await dbContext.TenantSubscriptions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ts => ts.StripeSubscriptionId == subscriptionId, ct);
            }

            if (sub == null && !string.IsNullOrWhiteSpace(customerId))
            {
                sub = await dbContext.TenantSubscriptions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ts => ts.StripeCustomerId == customerId, ct);
            }

            if (sub == null && !string.IsNullOrWhiteSpace(metadataTenantId) && Guid.TryParse(metadataTenantId, out var parsedTid))
            {
                sub = await dbContext.TenantSubscriptions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(ts => ts.TenantId == parsedTid, ct);
            }

            if (eventType.Equals("invoice.paid", StringComparison.OrdinalIgnoreCase))
            {
                var amountPaid = obj.TryGetProperty("amount_paid", out var a) ? a.GetInt32() : 0;
                var stripeInvoiceId = obj.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var hostedUrl = obj.TryGetProperty("hosted_invoice_url", out var hProp) ? hProp.GetString() : null;

                if (sub != null)
                {
                    sub.UpdateStatus("Active");
                    if (!string.IsNullOrWhiteSpace(customerId)) sub.SetStripeDetails(customerId, subscriptionId);

                    var invoice = new BillingInvoice(
                        Guid.NewGuid(),
                        sub.TenantId,
                        $"INV-{now:yyyyMM}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                        sub.PlanCode,
                        amountPaid,
                        "usd",
                        "Paid",
                        now,
                        now.AddMonths(1),
                        paidAtUtc: now,
                        stripeInvoiceId: stripeInvoiceId,
                        hostedInvoiceUrl: hostedUrl,
                        createdAtUtc: now);

                    dbContext.BillingInvoices.Add(invoice);
                }
            }
            else if (eventType.Equals("invoice.payment_failed", StringComparison.OrdinalIgnoreCase))
            {
                var amountDue = obj.TryGetProperty("amount_due", out var a) ? a.GetInt32() : 0;
                var stripeInvoiceId = obj.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

                if (sub != null)
                {
                    sub.UpdateStatus("PastDue");

                    var invoice = new BillingInvoice(
                        Guid.NewGuid(),
                        sub.TenantId,
                        $"INV-{now:yyyyMM}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                        sub.PlanCode,
                        amountDue,
                        "usd",
                        "Uncollectible",
                        now,
                        now.AddMonths(1),
                        paidAtUtc: null,
                        stripeInvoiceId: stripeInvoiceId,
                        hostedInvoiceUrl: null,
                        createdAtUtc: now);

                    dbContext.BillingInvoices.Add(invoice);
                }
            }
            else if (eventType.Equals("customer.subscription.deleted", StringComparison.OrdinalIgnoreCase))
            {
                if (sub != null)
                {
                    sub.UpdateStatus("Canceled");
                }
            }
        }

        dbContext.ProcessedStripeEvents.Add(new ProcessedStripeEvent(eventId, eventType, now));
        await dbContext.SaveChangesAsync(ct);
    }
}
