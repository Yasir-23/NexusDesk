namespace NexusDesk.Modules.Ticketing.RealTime;

/// <summary>
/// Tracks active staff viewers and typing state per tenant-scoped ticket room.
/// </summary>
public interface ITicketPresenceTracker
{
    void TrackJoin(Guid tenantId, Guid ticketId, Guid userId, string displayName, string connectionId);

    void TrackLeave(Guid tenantId, Guid ticketId, string connectionId);

    bool SetTyping(Guid tenantId, Guid ticketId, string connectionId, bool isTyping);

    IReadOnlyList<(Guid TenantId, Guid TicketId)> RemoveConnection(string connectionId);

    IReadOnlyList<TicketPresenceAgentDto> GetActiveAgents(Guid tenantId, Guid ticketId);
}
