using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Billing.Domain;

public sealed class BillingInvoice : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string InvoiceNumber { get; private set; }
    public string? StripeInvoiceId { get; private set; }
    public string PlanCode { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset PeriodStartUtc { get; private set; }
    public DateTimeOffset PeriodEndUtc { get; private set; }
    public DateTimeOffset? PaidAtUtc { get; private set; }
    public string? HostedInvoiceUrl { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public BillingInvoice(
        Guid id,
        Guid tenantId,
        string invoiceNumber,
        string planCode,
        int amountCents,
        string currency,
        string status,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        DateTimeOffset? paidAtUtc = null,
        string? stripeInvoiceId = null,
        string? hostedInvoiceUrl = null,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber ?? throw new ArgumentNullException(nameof(invoiceNumber));
        PlanCode = planCode ?? throw new ArgumentNullException(nameof(planCode));
        AmountCents = amountCents;
        Currency = currency ?? "usd";
        Status = status ?? throw new ArgumentNullException(nameof(status));
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        PaidAtUtc = paidAtUtc;
        StripeInvoiceId = stripeInvoiceId;
        HostedInvoiceUrl = hostedInvoiceUrl;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    private BillingInvoice() : base()
    {
        InvoiceNumber = string.Empty;
        PlanCode = string.Empty;
        Currency = "usd";
        Status = string.Empty;
    }

    public void MarkPaid(DateTimeOffset paidAtUtc, string? stripeInvoiceId = null, string? hostedInvoiceUrl = null)
    {
        Status = "Paid";
        PaidAtUtc = paidAtUtc;
        if (!string.IsNullOrWhiteSpace(stripeInvoiceId))
        {
            StripeInvoiceId = stripeInvoiceId;
        }
        if (!string.IsNullOrWhiteSpace(hostedInvoiceUrl))
        {
            HostedInvoiceUrl = hostedInvoiceUrl;
        }
    }

    public void MarkPaymentFailed(string? stripeInvoiceId = null)
    {
        Status = "Uncollectible";
        if (!string.IsNullOrWhiteSpace(stripeInvoiceId))
        {
            StripeInvoiceId = stripeInvoiceId;
        }
    }
}
