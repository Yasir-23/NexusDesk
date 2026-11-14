namespace NexusDesk.Modules.Billing.Domain;

public sealed class ProcessedStripeEvent
{
    public string EventId { get; private set; }
    public string EventType { get; private set; }
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public ProcessedStripeEvent(string eventId, string eventType, DateTimeOffset processedAtUtc)
    {
        EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        ProcessedAtUtc = processedAtUtc;
    }

    private ProcessedStripeEvent()
    {
        EventId = string.Empty;
        EventType = string.Empty;
    }
}
