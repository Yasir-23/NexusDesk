using System.Text.Json.Serialization;

namespace NexusDesk.Modules.Ticketing.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TicketChannel
{
    WebPortal = 1,
    Email = 2,
    Api = 3,
    LiveChat = 4
}
