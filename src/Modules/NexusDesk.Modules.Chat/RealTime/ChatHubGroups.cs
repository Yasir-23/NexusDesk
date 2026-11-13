namespace NexusDesk.Modules.Chat.RealTime;

public static class ChatHubGroups
{
    public static string Session(Guid tenantId, Guid sessionId) => $"tenant:{tenantId}:chat:{sessionId}";
    public static string TenantStaff(Guid tenantId) => $"tenant:{tenantId}:chat:staff";
    public static string TenantAll(Guid tenantId) => $"tenant:{tenantId}:chat:all";
}
