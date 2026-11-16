using System.Security.Claims;
using HotChocolate;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Analytics.Services;
using NexusDesk.Modules.Billing.Persistence;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.KnowledgeBase.Domain;
using NexusDesk.Modules.KnowledgeBase.Persistence;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Api.GraphQL;

public class Query
{
    public async Task<IReadOnlyList<TicketGqlType>> GetTicketsAsync(
        int? first,
        string? status,
        [Service] ITenantContextAccessor tenantContextAccessor,
        [Service] TicketingDbContext ticketingDb,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated(claimsPrincipal);
        var tenantId = EnsureTenantResolved(tenantContextAccessor);

        var query = ticketingDb.Tickets
            .AsNoTracking()
            .Include(t => t.Tags)
            .Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(t => t.Status == parsedStatus);
        }

        var take = first is null or <= 0 ? 50 : Math.Min(first.Value, 100);

        var tickets = await query
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        return tickets.Select(t => new TicketGqlType(
            t.Id,
            t.TicketNumber,
            t.Title,
            t.Description,
            t.Status.ToString(),
            t.Priority.ToString(),
            t.Channel.ToString(),
            t.CustomerId,
            t.AssignedAgentId,
            t.IsSlaBreached,
            t.FirstResponseDueUtc,
            t.ResolutionDueUtc,
            t.Tags.Select(tag => tag.Name).ToList(),
            t.CreatedAtUtc,
            t.UpdatedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<KbArticleGqlType>> GetKbArticlesAsync(
        string? search,
        [Service] ITenantContextAccessor tenantContextAccessor,
        [Service] KnowledgeBaseDbContext kbDb,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated(claimsPrincipal);
        var tenantId = EnsureTenantResolved(tenantContextAccessor);

        var query = kbDb.KbArticles
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Status == KbArticleStatus.Published);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(s) || a.BodyMarkdown.ToLower().Contains(s));
        }

        var articles = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        return articles.Select(a => new KbArticleGqlType(
            a.Id,
            a.TenantId,
            a.CategoryId,
            a.Title,
            a.Slug,
            a.BodyMarkdown,
            a.Status == KbArticleStatus.Published,
            a.CreatedAtUtc,
            a.UpdatedAtUtc)).ToList();
    }

    public async Task<BillingOverviewGqlType> GetBillingOverviewAsync(
        [Service] ITenantContextAccessor tenantContextAccessor,
        [Service] BillingDbContext billingDb,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated(claimsPrincipal);
        EnsureAuthorized(claimsPrincipal, IdentityRoles.TenantAdmin, IdentityPermissions.BillingRead);
        var tenantId = EnsureTenantResolved(tenantContextAccessor);

        var subscription = await billingDb.TenantSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var planCode = subscription?.PlanCode ?? "Free";
        var plan = await billingDb.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);

        var periodKey = DateTimeOffset.UtcNow.ToString("yyyy-MM");
        var usageRecords = await billingDb.UsageMeterRecords
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.PeriodKey == periodKey)
            .ToListAsync(cancellationToken);

        var ticketsCreated = usageRecords.FirstOrDefault(u => u.MetricName.Equals("tickets_created", StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0L;
        var activeAgents = usageRecords.FirstOrDefault(u => u.MetricName.Equals("active_agents", StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0L;
        var aiTokens = usageRecords.FirstOrDefault(u => u.MetricName.Equals("ai_tokens_used", StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0L;

        return new BillingOverviewGqlType(
            planCode,
            plan?.Name ?? planCode,
            subscription?.Status ?? "Active",
            subscription?.CurrentPeriodStartUtc ?? DateTimeOffset.UtcNow,
            subscription?.CurrentPeriodEndUtc ?? DateTimeOffset.UtcNow.AddDays(30),
            plan?.MaxAgents ?? 2,
            plan?.MaxMonthlyTickets ?? 100,
            plan?.MaxMonthlyAiTokens ?? 0L,
            plan?.MonthlyPriceCents ?? 0,
            ticketsCreated,
            activeAgents,
            aiTokens,
            subscription?.CancelAtPeriodEnd ?? false);
    }

    public async Task<AnalyticsOverviewGqlType> GetAnalyticsOverviewAsync(
        int? days,
        [Service] ITenantContextAccessor tenantContextAccessor,
        [Service] IAnalyticsService analyticsService,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated(claimsPrincipal);
        EnsureAuthorized(claimsPrincipal, IdentityRoles.Agent, IdentityPermissions.AnalyticsRead);
        var tenantId = EnsureTenantResolved(tenantContextAccessor);

        var daysCount = days is null or <= 0 ? 30 : days.Value;
        var overview = await analyticsService.GetOverviewAsync(tenantId, daysCount, cancellationToken);

        return new AnalyticsOverviewGqlType(
            overview.TotalTickets,
            overview.OpenTickets,
            overview.ResolvedTickets,
            overview.SlaComplianceRate,
            overview.AverageFirstResponseMinutes,
            overview.AverageResolutionMinutes,
            overview.CsatAverageScore,
            overview.ActiveChatSessions,
            overview.PublishedKbArticles);
    }

    public Task<AnalyticsOverviewGqlType> GetAnalyticsSummaryAsync(
        [Service] ITenantContextAccessor tenantContextAccessor,
        [Service] IAnalyticsService analyticsService,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        return GetAnalyticsOverviewAsync(30, tenantContextAccessor, analyticsService, claimsPrincipal, cancellationToken);
    }

    private static void EnsureAuthenticated(ClaimsPrincipal claimsPrincipal)
    {
        if (claimsPrincipal.Identity?.IsAuthenticated != true)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Unauthorized: Authenticated JWT token is required.")
                .SetCode("UNAUTHORIZED")
                .Build());
        }
    }

    private static Guid EnsureTenantResolved(ITenantContextAccessor tenantContextAccessor)
    {
        var context = tenantContextAccessor.TenantContext;
        if (context == null || !context.IsResolved || !context.TenantId.HasValue || context.TenantId.Value == Guid.Empty)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Tenant Not Resolved: A valid tenant context is required.")
                .SetCode("TENANT_NOT_RESOLVED")
                .Build());
        }
        return context.TenantId.Value;
    }

    private static void EnsureAuthorized(ClaimsPrincipal claimsPrincipal, string minimumRole, string requiredPermission)
    {
        var isSuperAdmin = claimsPrincipal.IsInRole(IdentityRoles.SuperAdmin);
        if (isSuperAdmin)
        {
            return;
        }

        var hasPermission = claimsPrincipal.HasClaim("permission", requiredPermission);
        if (hasPermission)
        {
            return;
        }

        if (minimumRole == IdentityRoles.Agent)
        {
            if (claimsPrincipal.IsInRole(IdentityRoles.Agent) || claimsPrincipal.IsInRole(IdentityRoles.TenantAdmin))
            {
                return;
            }
        }
        else if (minimumRole == IdentityRoles.TenantAdmin)
        {
            if (claimsPrincipal.IsInRole(IdentityRoles.TenantAdmin))
            {
                return;
            }
        }

        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage("Forbidden: Insufficient privileges for this query.")
            .SetCode("FORBIDDEN")
            .Build());
    }
}
