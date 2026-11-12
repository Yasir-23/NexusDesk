namespace NexusDesk.Modules.Ticketing.RealTime;

public static class TicketHubGroups
{
    public static string Tenant(Guid tenantId) => $"tenant:{tenantId:D}";

    public static string TenantStaff(Guid tenantId) => $"tenant:{tenantId:D}:staff";

    public static string TenantUser(Guid tenantId, Guid userId) => $"tenant:{tenantId:D}:user:{userId:D}";

    public static string TicketRoom(Guid tenantId, Guid ticketId) => $"tenant:{tenantId:D}:ticket:{ticketId:D}";

    public static string TicketRoomStaff(Guid tenantId, Guid ticketId) => $"tenant:{tenantId:D}:ticket:{ticketId:D}:staff";
}
