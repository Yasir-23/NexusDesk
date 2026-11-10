using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Billing.Domain;

public sealed class Invoice : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string BillingPeriod { get; private set; }
    public decimal AmountDue { get; private set; }
    public string Status { get; private set; }

    public Invoice(Guid id, Guid tenantId, string billingPeriod, decimal amountDue, string status) : base(id)
    {
        TenantId = tenantId;
        BillingPeriod = billingPeriod ?? throw new ArgumentNullException(nameof(billingPeriod));
        AmountDue = amountDue;
        Status = status ?? throw new ArgumentNullException(nameof(status));
    }

    private Invoice() : base()
    {
        BillingPeriod = string.Empty;
        Status = string.Empty;
    }
}
