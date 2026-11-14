using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Billing.Domain;

public sealed class TenantSubscription : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string PlanCode { get; private set; }
    public string Status { get; private set; }
    public string BillingCycle { get; private set; }
    public string? StripeCustomerId { get; private set; }
    public string? StripeSubscriptionId { get; private set; }
    public DateTimeOffset CurrentPeriodStartUtc { get; private set; }
    public DateTimeOffset CurrentPeriodEndUtc { get; private set; }
    public bool CancelAtPeriodEnd { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public TenantSubscription(
        Guid id,
        Guid tenantId,
        string planCode,
        string status,
        string billingCycle,
        DateTimeOffset currentPeriodStartUtc,
        DateTimeOffset currentPeriodEndUtc,
        string? stripeCustomerId = null,
        string? stripeSubscriptionId = null,
        bool cancelAtPeriodEnd = false,
        DateTimeOffset? updatedAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        PlanCode = planCode ?? throw new ArgumentNullException(nameof(planCode));
        Status = status ?? throw new ArgumentNullException(nameof(status));
        BillingCycle = billingCycle ?? throw new ArgumentNullException(nameof(billingCycle));
        CurrentPeriodStartUtc = currentPeriodStartUtc;
        CurrentPeriodEndUtc = currentPeriodEndUtc;
        StripeCustomerId = stripeCustomerId;
        StripeSubscriptionId = stripeSubscriptionId;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow;
    }

    private TenantSubscription() : base()
    {
        PlanCode = string.Empty;
        Status = string.Empty;
        BillingCycle = string.Empty;
    }

    public void ChangePlan(string newPlanCode, string? newBillingCycle = null)
    {
        PlanCode = newPlanCode ?? throw new ArgumentNullException(nameof(newPlanCode));
        if (!string.IsNullOrWhiteSpace(newBillingCycle))
        {
            BillingCycle = newBillingCycle;
        }
        Status = "Active";
        CancelAtPeriodEnd = false;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Cancel(bool immediate = false)
    {
        if (immediate)
        {
            Status = "Canceled";
            CancelAtPeriodEnd = false;
        }
        else
        {
            CancelAtPeriodEnd = true;
        }
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateStatus(string newStatus)
    {
        Status = newStatus ?? throw new ArgumentNullException(nameof(newStatus));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetStripeDetails(string? customerId, string? subscriptionId)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            StripeCustomerId = customerId;
        }
        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            StripeSubscriptionId = subscriptionId;
        }
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RenewPeriod(DateTimeOffset newStart, DateTimeOffset newEnd)
    {
        CurrentPeriodStartUtc = newStart;
        CurrentPeriodEndUtc = newEnd;
        Status = "Active";
        CancelAtPeriodEnd = false;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
