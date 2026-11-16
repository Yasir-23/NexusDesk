using System.Text;
using NexusDesk.SharedKernel.Analytics;

namespace NexusDesk.Modules.Analytics.Services;

public interface IAnalyticsService
{
    Task<TenantAnalyticsOverviewDto> GetOverviewAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyTicketTrendDto>> GetTicketTrendsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
    Task<string> ExportTicketsCsvAsync(Guid tenantId, int days, CancellationToken cancellationToken = default);
}

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly ITicketingAnalyticsProvider _ticketingProvider;
    private readonly IChatAnalyticsProvider? _chatProvider;
    private readonly IKnowledgeBaseAnalyticsProvider? _kbProvider;

    public AnalyticsService(
        ITicketingAnalyticsProvider ticketingProvider,
        IChatAnalyticsProvider? chatProvider = null,
        IKnowledgeBaseAnalyticsProvider? kbProvider = null)
    {
        _ticketingProvider = ticketingProvider;
        _chatProvider = chatProvider;
        _kbProvider = kbProvider;
    }

    public async Task<TenantAnalyticsOverviewDto> GetOverviewAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        var metrics = await _ticketingProvider.GetOverviewMetricsAsync(tenantId, days, cancellationToken);
        var activeChats = _chatProvider != null ? await _chatProvider.GetActiveChatSessionsCountAsync(tenantId, cancellationToken) : 0;
        var publishedArticles = _kbProvider != null ? await _kbProvider.GetPublishedArticlesCountAsync(tenantId, cancellationToken) : 0;

        return new TenantAnalyticsOverviewDto(
            metrics.TotalTickets,
            metrics.OpenTickets,
            metrics.ResolvedTickets,
            metrics.SlaComplianceRate,
            metrics.AverageFirstResponseMinutes,
            metrics.AverageResolutionMinutes,
            metrics.CsatAverageScore,
            activeChats,
            publishedArticles);
    }

    public async Task<IReadOnlyList<DailyTicketTrendDto>> GetTicketTrendsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        return await _ticketingProvider.GetTicketTrendsAsync(tenantId, days, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        return await _ticketingProvider.GetAgentPerformanceAsync(tenantId, days, cancellationToken);
    }

    public async Task<string> ExportTicketsCsvAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        var rows = await _ticketingProvider.GetTicketsForExportAsync(tenantId, days, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("TicketNumber,Title,Status,Priority,Category,CustomerEmail,AssignedAgentEmail,IsSlaBreached,CreatedAtUtc,ResolvedAtUtc");

        foreach (var r in rows)
        {
            sb.Append(EscapeCsv(r.TicketNumber)).Append(',');
            sb.Append(EscapeCsv(r.Title)).Append(',');
            sb.Append(EscapeCsv(r.Status)).Append(',');
            sb.Append(EscapeCsv(r.Priority)).Append(',');
            sb.Append(EscapeCsv(r.Category)).Append(',');
            sb.Append(EscapeCsv(r.CustomerEmail)).Append(',');
            sb.Append(EscapeCsv(r.AssignedAgentEmail ?? string.Empty)).Append(',');
            sb.Append(r.IsSlaBreached ? "true" : "false").Append(',');
            sb.Append(r.CreatedAtUtc.ToString("o")).Append(',');
            sb.Append(r.ResolvedAtUtc.HasValue ? r.ResolvedAtUtc.Value.ToString("o") : string.Empty);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
