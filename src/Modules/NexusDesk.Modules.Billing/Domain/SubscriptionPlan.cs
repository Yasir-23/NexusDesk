using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Billing.Domain;

public sealed class SubscriptionPlan : Entity<Guid>
{
    public string Code { get; private set; }
    public string Name { get; private set; }
    public int MonthlyPriceCents { get; private set; }
    public int MaxAgents { get; private set; }
    public int MaxMonthlyTickets { get; private set; }
    public long MaxMonthlyAiTokens { get; private set; }
    public bool CustomBrandingEnabled { get; private set; }
    public bool WebhooksEnabled { get; private set; }
    public bool SlaEnabled { get; private set; }
    public bool IsActive { get; private set; }

    public SubscriptionPlan(
        Guid id,
        string code,
        string name,
        int monthlyPriceCents,
        int maxAgents,
        int maxMonthlyTickets,
        long maxMonthlyAiTokens,
        bool customBrandingEnabled,
        bool webhooksEnabled,
        bool slaEnabled,
        bool isActive = true) : base(id)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MonthlyPriceCents = monthlyPriceCents;
        MaxAgents = maxAgents;
        MaxMonthlyTickets = maxMonthlyTickets;
        MaxMonthlyAiTokens = maxMonthlyAiTokens;
        CustomBrandingEnabled = customBrandingEnabled;
        WebhooksEnabled = webhooksEnabled;
        SlaEnabled = slaEnabled;
        IsActive = isActive;
    }

    private SubscriptionPlan() : base()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public void UpdatePricingAndLimits(
        string name,
        int monthlyPriceCents,
        int maxAgents,
        int maxMonthlyTickets,
        long maxMonthlyAiTokens,
        bool customBrandingEnabled,
        bool webhooksEnabled,
        bool slaEnabled,
        bool isActive)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MonthlyPriceCents = monthlyPriceCents;
        MaxAgents = maxAgents;
        MaxMonthlyTickets = maxMonthlyTickets;
        MaxMonthlyAiTokens = maxMonthlyAiTokens;
        CustomBrandingEnabled = customBrandingEnabled;
        WebhooksEnabled = webhooksEnabled;
        SlaEnabled = slaEnabled;
        IsActive = isActive;
    }
}
