using System.Text.Json.Serialization;

namespace NexusDesk.Modules.Ticketing.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketStatus
{
    New = 1,
    Open = 2,
    PendingCustomer = 3,
    PendingInternal = 4,
    Resolved = 5,
    Closed = 6
}
