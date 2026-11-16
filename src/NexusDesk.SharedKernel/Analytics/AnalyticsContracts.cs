namespace NexusDesk.SharedKernel.Analytics;

public sealed record TenantAnalyticsOverviewDto(
    int TotalTickets,
    int OpenTickets,
    int ResolvedTickets,
    double SlaComplianceRate,
    double AverageFirstResponseMinutes,
    double AverageResolutionMinutes,
    double CsatAverageScore,
    int ActiveChatSessions,
    int PublishedKbArticles);

public sealed record DailyTicketTrendDto(
    string Date,
    int CreatedCount,
    int ResolvedCount,
    int BreachedCount);

public sealed record AgentPerformanceDto(
    Guid AgentUserId,
    string AgentName,
    int AssignedTickets,
    int ResolvedTickets,
    double AverageResolutionMinutes,
    double SlaComplianceRate);

public sealed record TicketExportRowDto(
    string TicketNumber,
    string Title,
    string Status,
    string Priority,
    string Category,
    string CustomerEmail,
    string? AssignedAgentEmail,
    bool IsSlaBreached,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public interface ITicketingAnalyticsProvider
{
    Task<TenantAnalyticsOverviewDto> GetOverviewMetricsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyTicketTrendDto>> GetTicketTrendsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TicketExportRowDto>> GetTicketsForExportAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
}

public interface IChatAnalyticsProvider
{
    Task<int> GetActiveChatSessionsCountAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public interface IKnowledgeBaseAnalyticsProvider
{
    Task<int> GetPublishedArticlesCountAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
