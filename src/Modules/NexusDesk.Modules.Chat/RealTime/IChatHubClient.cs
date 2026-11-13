using NexusDesk.Modules.Chat.Contracts;

namespace NexusDesk.Modules.Chat.RealTime;

public interface IChatHubClient
{
    Task ChatMessageReceived(ChatMessageDto message);
    Task ChatSessionClaimed(ChatSessionDto session);
    Task ChatSessionClosed(ChatSessionClosedDto session);
    Task ChatTypingIndicator(ChatTypingDto indicator);
}
