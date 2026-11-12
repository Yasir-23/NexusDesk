namespace NexusDesk.Modules.Ticketing.RealTime;

public interface ITicketHubClient
{
    Task TicketCreated(TicketRealtimeNotification payload);
    Task TicketUpdated(TicketRealtimeNotification payload);
    Task TicketCommentAdded(TicketCommentRealtimeNotification payload);
    Task AgentPresenceUpdated(TicketPresenceNotification payload);
    Task SlaBreachAlert(SlaBreachAlertNotification payload);
}
