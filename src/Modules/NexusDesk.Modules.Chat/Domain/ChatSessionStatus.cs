using System.Text.Json.Serialization;

namespace NexusDesk.Modules.Chat.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatSessionStatus
{
    Waiting = 1,
    Active = 2,
    Closed = 3,
    ConvertedToTicket = 4
}
