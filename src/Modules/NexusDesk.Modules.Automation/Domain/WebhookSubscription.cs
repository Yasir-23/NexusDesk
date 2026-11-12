using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Domain;

public sealed class WebhookSubscription : Entity<Guid>, ITenantScoped
{
    public static readonly IReadOnlySet<string> ValidEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ticket.created",
        "ticket.sla_breached",
        "ticket.resolved",
        "*"
    };

    public Guid TenantId { get; private set; }
    public string TargetUrl { get; private set; }
    public string EncryptedSecret { get; private set; }
    public IReadOnlyList<string> EventTypes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public WebhookSubscription(
        Guid id,
        Guid tenantId,
        string targetUrl,
        string encryptedSecret,
        IEnumerable<string> eventTypes,
        bool isActive,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? updatedAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        TargetUrl = targetUrl;
        EncryptedSecret = encryptedSecret;
        EventTypes = eventTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    private WebhookSubscription() : base()
    {
        TargetUrl = string.Empty;
        EncryptedSecret = string.Empty;
        EventTypes = [];
    }

    public static Result<WebhookSubscription> Create(
        Guid tenantId,
        string targetUrl,
        string encryptedSecret,
        IEnumerable<string> eventTypes,
        bool isActive = true,
        DateTimeOffset? utcNow = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<WebhookSubscription>(Error.Validation("Webhook.InvalidTenant", "TenantId is required."));
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return Result.Failure<WebhookSubscription>(Error.Validation("Webhook.InvalidTargetUrl", "Target URL is required."));
        }

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            return Result.Failure<WebhookSubscription>(Error.Validation("Webhook.InvalidTargetUrl", "Target URL must be a valid absolute URI."));
        }

        var isHttps = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLocalHttp = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                          (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

        if (!isHttps && !isLocalHttp)
        {
            return Result.Failure<WebhookSubscription>(Error.Validation(
                "Webhook.InvalidTargetUrlScheme",
                "Target URL must use HTTPS (HTTP is only permitted for localhost during testing)."));
        }

        if (string.IsNullOrWhiteSpace(encryptedSecret))
        {
            return Result.Failure<WebhookSubscription>(Error.Validation("Webhook.InvalidSecret", "Encrypted secret is required."));
        }

        var rawEventTypes = eventTypes?.ToList() ?? [];
        if (rawEventTypes.Count == 0)
        {
            return Result.Failure<WebhookSubscription>(Error.Validation("Webhook.EmptyEventTypes", "At least one event type must be subscribed to."));
        }

        var normalizedEventTypes = new List<string>();
        foreach (var eventType in rawEventTypes)
        {
            var trimmed = eventType?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(trimmed) || !ValidEventTypes.Contains(trimmed))
            {
                return Result.Failure<WebhookSubscription>(Error.Validation(
                    "Webhook.InvalidEventType",
                    $"Event type '{eventType}' is not supported. Supported event types are: {string.Join(", ", ValidEventTypes)}."));
            }
            normalizedEventTypes.Add(trimmed);
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        return Result.Success(new WebhookSubscription(
            Guid.NewGuid(),
            tenantId,
            uri.ToString(),
            encryptedSecret,
            normalizedEventTypes,
            isActive,
            now));
    }

    public void Toggle(DateTimeOffset? utcNow = null)
    {
        IsActive = !IsActive;
        UpdatedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
    }

    public bool MatchesEvent(string eventType)
    {
        if (!IsActive)
        {
            return false;
        }

        return EventTypes.Any(e =>
            string.Equals(e, "*", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e, eventType, StringComparison.OrdinalIgnoreCase));
    }
}
