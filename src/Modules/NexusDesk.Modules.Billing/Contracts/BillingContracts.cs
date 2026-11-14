namespace NexusDesk.Modules.Billing.Contracts;

public sealed record SubscriptionPlanDto(
    Guid Id,
    string Code,
    string Name,
    int MonthlyPriceCents,
    int MaxAgents,
    int MaxMonthlyTickets,
    long MaxMonthlyAiTokens,
    bool CustomBrandingEnabled,
    bool WebhooksEnabled,
    bool SlaEnabled,
    bool IsActive);

public sealed record TenantSubscriptionDto(
    Guid Id,
    Guid TenantId,
    string PlanCode,
    string Status,
    string BillingCycle,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    bool CancelAtPeriodEnd,
    DateTimeOffset UpdatedAtUtc);

public sealed record MetricUsageDto(
    string MetricName,
    long Used,
    long Limit,
    long Remaining,
    double UsagePercentage,
    double RemainingPercentage);

public sealed record TenantSubscriptionDetailsDto(
    TenantSubscriptionDto Subscription,
    SubscriptionPlanDto Plan,
    string PeriodKey,
    IReadOnlyList<MetricUsageDto> Usage);

public sealed record ChangePlanRequest(
    string PlanCode,
    string? BillingCycle);

public sealed record CancelSubscriptionRequest(
    bool? Immediate);

public sealed record RecordUsageRequest(
    string MetricName,
    long Quantity);

public sealed record UsagePeriodDto(
    string PeriodKey,
    string PlanCode,
    IReadOnlyList<MetricUsageDto> Metrics);

public sealed record BillingInvoiceDto(
    Guid Id,
    Guid TenantId,
    string InvoiceNumber,
    string? StripeInvoiceId,
    string PlanCode,
    int AmountCents,
    string Currency,
    string Status,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    DateTimeOffset? PaidAtUtc,
    string? HostedInvoiceUrl,
    DateTimeOffset CreatedAtUtc);
