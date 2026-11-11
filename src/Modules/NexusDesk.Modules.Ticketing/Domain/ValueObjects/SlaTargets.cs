using NexusDesk.Modules.Ticketing.Domain.Enums;

namespace NexusDesk.Modules.Ticketing.Domain.ValueObjects;

/// <summary>
/// Represents the calculated SLA target timestamps for a ticket.
/// </summary>
public readonly record struct SlaTargets(
    DateTimeOffset FirstResponseDueUtc,
    DateTimeOffset ResolutionDueUtc);

/// <summary>
/// Calculates SLA response and resolution deadlines based on ticket priority.
/// </summary>
public static class SlaPolicyMatrix
{
    public static (TimeSpan FirstResponseWindow, TimeSpan ResolutionWindow) GetWindows(TicketPriority priority)
    {
        return priority switch
        {
            TicketPriority.Urgent => (TimeSpan.FromHours(1), TimeSpan.FromHours(4)),
            TicketPriority.High => (TimeSpan.FromHours(2), TimeSpan.FromHours(8)),
            TicketPriority.Medium => (TimeSpan.FromHours(8), TimeSpan.FromHours(24)),
            TicketPriority.Low => (TimeSpan.FromHours(24), TimeSpan.FromHours(72)),
            _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unsupported ticket priority.")
        };
    }

    public static SlaTargets CalculateTargets(TicketPriority priority, DateTimeOffset createdAtUtc)
    {
        var (firstResponseWindow, resolutionWindow) = GetWindows(priority);
        return new SlaTargets(
            createdAtUtc.Add(firstResponseWindow),
            createdAtUtc.Add(resolutionWindow));
    }
}
