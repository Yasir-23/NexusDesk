using System.Text.Json.Serialization;

namespace NexusDesk.Modules.Ticketing.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommentType
{
    PublicReply = 1,
    InternalNote = 2
}
