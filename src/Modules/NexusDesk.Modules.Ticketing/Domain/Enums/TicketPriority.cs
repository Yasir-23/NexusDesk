using System.Text.Json.Serialization;

namespace NexusDesk.Modules.Ticketing.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4
}
