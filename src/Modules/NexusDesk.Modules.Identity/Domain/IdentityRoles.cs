namespace NexusDesk.Modules.Identity.Domain;

public static class IdentityRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Agent = "Agent";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, TenantAdmin, Agent, Customer];

    public static bool IsValid(string? role) =>
        role != null && All.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public static class IdentityPermissions
{
    public const string PlatformManage = "platform:manage";
    public const string TenantsManage = "tenants:manage";
    public const string AgentsManage = "agents:manage";
    public const string SettingsManage = "settings:manage";
    public const string TicketsRead = "tickets:read";
    public const string TicketsWrite = "tickets:write";
    public const string KnowledgeBaseManage = "kb:manage";
    public const string KnowledgeBaseRead = "kb:read";
    public const string AnalyticsRead = "analytics:read";

    public static string[] GetPermissionsForRole(string role) => role switch
    {
        IdentityRoles.SuperAdmin => [PlatformManage, TenantsManage, AgentsManage, SettingsManage, TicketsRead, TicketsWrite, KnowledgeBaseManage, KnowledgeBaseRead, AnalyticsRead],
        IdentityRoles.TenantAdmin => [AgentsManage, SettingsManage, TicketsRead, TicketsWrite, KnowledgeBaseManage, KnowledgeBaseRead, AnalyticsRead],
        IdentityRoles.Agent => [TicketsRead, TicketsWrite, KnowledgeBaseManage, KnowledgeBaseRead, AnalyticsRead],
        IdentityRoles.Customer => [TicketsRead, TicketsWrite, KnowledgeBaseRead],
        _ => []
    };
}
