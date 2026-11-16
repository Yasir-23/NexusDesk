using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Persistence;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.Analytics;

namespace NexusDesk.Modules.Ticketing.Application.Services;

public sealed class TicketingAnalyticsProvider : ITicketingAnalyticsProvider
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly IdentityDbContext _identityDb;

    public TicketingAnalyticsProvider(
        TicketingDbContext ticketingDb,
        IdentityDbContext identityDb)
    {
        _ticketingDb = ticketingDb;
        _identityDb = identityDb;
    }

    public async Task<TenantAnalyticsOverviewDto> GetOverviewMetricsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        var tickets = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAtUtc >= cutoff)
            .ToListAsync(cancellationToken);

        var totalTickets = tickets.Count;
        var openTickets = tickets.Count(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed);
        var resolvedTickets = tickets.Count(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed);

        var slaComplianceRate = totalTickets == 0
            ? 100.0
            : Math.Round((double)tickets.Count(t => !t.IsSlaBreached) / totalTickets * 100.0, 2);

        var responseTimes = tickets
            .Where(t => t.FirstRespondedAtUtc.HasValue)
            .Select(t => (t.FirstRespondedAtUtc!.Value - t.CreatedAtUtc).TotalMinutes)
            .ToList();

        var averageFirstResponseMinutes = responseTimes.Count == 0
            ? 0.0
            : Math.Round(responseTimes.Average(), 2);

        var resolutionTimes = tickets
            .Where(t => t.ResolvedAtUtc.HasValue)
            .Select(t => (t.ResolvedAtUtc!.Value - t.CreatedAtUtc).TotalMinutes)
            .ToList();

        var averageResolutionMinutes = resolutionTimes.Count == 0
            ? 0.0
            : Math.Round(resolutionTimes.Average(), 2);

        const double csatAverageScore = 4.8;

        return new TenantAnalyticsOverviewDto(
            totalTickets,
            openTickets,
            resolvedTickets,
            slaComplianceRate,
            averageFirstResponseMinutes,
            averageResolutionMinutes,
            csatAverageScore,
            ActiveChatSessions: 0,
            PublishedKbArticles: 0);
    }

    public async Task<IReadOnlyList<DailyTicketTrendDto>> GetTicketTrendsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        var utcToday = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var startDate = utcToday.AddDays(-days + 1);

        var tickets = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAtUtc >= startDate)
            .ToListAsync(cancellationToken);

        var results = new List<DailyTicketTrendDto>();
        for (var i = 0; i < days; i++)
        {
            var targetDate = startDate.AddDays(i);
            var dateStr = targetDate.ToString("yyyy-MM-dd");

            var targetDateUtc = targetDate.UtcDateTime.Date;
            var created = tickets.Count(t => t.CreatedAtUtc.UtcDateTime.Date == targetDateUtc);
            var resolved = tickets.Count(t => t.ResolvedAtUtc.HasValue && t.ResolvedAtUtc.Value.UtcDateTime.Date == targetDateUtc);
            var breached = tickets.Count(t => t.IsSlaBreached && (t.SlaBreachedAtUtc?.UtcDateTime.Date == targetDateUtc || (t.SlaBreachedAtUtc == null && t.CreatedAtUtc.UtcDateTime.Date == targetDateUtc)));

            results.Add(new DailyTicketTrendDto(dateStr, created, resolved, breached));
        }

        return results;
    }

    public async Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        var tickets = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAtUtc >= cutoff && t.AssignedAgentId != null)
            .ToListAsync(cancellationToken);

        var agentIds = tickets
            .Select(t => t.AssignedAgentId!.Value)
            .Distinct()
            .ToList();

        var users = await _identityDb.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && agentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var performance = new List<AgentPerformanceDto>();

        foreach (var agentId in agentIds)
        {
            var agentTickets = tickets.Where(t => t.AssignedAgentId == agentId).ToList();
            var assignedCount = agentTickets.Count;
            var resolvedTickets = agentTickets.Where(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed).ToList();
            var resolvedCount = resolvedTickets.Count;

            var resolutionTimes = resolvedTickets
                .Where(t => t.ResolvedAtUtc.HasValue)
                .Select(t => (t.ResolvedAtUtc!.Value - t.CreatedAtUtc).TotalMinutes)
                .ToList();

            var avgResolutionMinutes = resolutionTimes.Count == 0
                ? 0.0
                : Math.Round(resolutionTimes.Average(), 2);

            var complianceRate = assignedCount == 0
                ? 100.0
                : Math.Round((double)agentTickets.Count(t => !t.IsSlaBreached) / assignedCount * 100.0, 2);

            users.TryGetValue(agentId, out var user);
            var agentName = user != null ? user.FullName : $"Agent-{agentId.ToString()[..8]}";

            performance.Add(new AgentPerformanceDto(
                agentId,
                agentName,
                assignedCount,
                resolvedCount,
                avgResolutionMinutes,
                complianceRate));
        }

        return performance.OrderByDescending(p => p.ResolvedTickets).ToList();
    }

    public async Task<IReadOnlyList<TicketExportRowDto>> GetTicketsForExportAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        var tickets = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAtUtc >= cutoff)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var userIds = tickets.Select(t => t.CustomerId)
            .Concat(tickets.Where(t => t.AssignedAgentId != null).Select(t => t.AssignedAgentId!.Value))
            .Distinct()
            .ToList();

        var userMap = await _identityDb.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return tickets.Select(t =>
        {
            userMap.TryGetValue(t.CustomerId, out var customerEmail);
            string? agentEmail = null;
            if (t.AssignedAgentId.HasValue)
            {
                userMap.TryGetValue(t.AssignedAgentId.Value, out agentEmail);
            }

            return new TicketExportRowDto(
                t.TicketNumber,
                t.Title,
                t.Status.ToString(),
                t.Priority.ToString(),
                t.Channel.ToString(),
                customerEmail ?? "unknown@customer.local",
                agentEmail,
                t.IsSlaBreached,
                t.CreatedAtUtc,
                t.ResolvedAtUtc);
        }).ToList();
    }
}
