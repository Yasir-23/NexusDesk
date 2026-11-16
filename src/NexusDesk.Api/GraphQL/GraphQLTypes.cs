namespace NexusDesk.Api.GraphQL;

public sealed record TicketGqlType(
    Guid Id,
    string TicketNumber,
    string Title,
    string Description,
    string Status,
    string Priority,
    string Channel,
    Guid CustomerId,
    Guid? AssignedAgentId,
    bool IsSlaBreached,
    DateTimeOffset? FirstResponseDueAtUtc,
    DateTimeOffset? ResolutionDueAtUtc,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record KbArticleGqlType(
    Guid Id,
    Guid TenantId,
    Guid CategoryId,
    string Title,
    string Slug,
    string ContentMarkdown,
    bool IsPublished,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record BillingOverviewGqlType(
    string PlanCode,
    string PlanName,
    string Status,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    int MaxAgents,
    int MaxMonthlyTickets,
    long MaxMonthlyAiTokens,
    int MonthlyPriceCents,
    long TicketsCreatedThisPeriod,
    long ActiveAgents,
    long AiTokensUsedThisPeriod,
    bool CancelAtPeriodEnd);

public sealed record AnalyticsOverviewGqlType(
    int TotalTickets,
    int OpenTickets,
    int ResolvedTickets,
    double SlaComplianceRate,
    double AverageFirstResponseMinutes,
    double AverageResolutionMinutes,
    double CsatAverageScore,
    int ActiveChatSessions,
    int PublishedKbArticles);
