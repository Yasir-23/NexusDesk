using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Chat.Contracts;
using NexusDesk.Modules.Chat.Persistence;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Chat.RealTime;

[Authorize]
public sealed class ChatHub : Hub<IChatHubClient>
{
    private readonly ChatDbContext _chatDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public ChatHub(
        ChatDbContext chatDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _chatDb = chatDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryGetCallerClaims(out var tenantId, out var userId, out _, out var isStaff))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ChatHubGroups.TenantAll(tenantId));

        if (isStaff)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ChatHubGroups.TenantStaff(tenantId));
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinChatSession(Guid sessionId)
    {
        if (!TryGetCallerClaims(out var tenantId, out var userId, out _, out var isStaff))
        {
            throw new HubException("Unauthorized: Tenant or user could not be resolved.");
        }

        EnsureTenantContext(tenantId);

        var session = await _chatDb.ChatSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId && s.TenantId == tenantId)
            .Select(s => new { s.Id, s.CustomerId })
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        if (session == null)
        {
            throw new HubException("Chat session was not found.");
        }

        if (!isStaff && session.CustomerId != userId)
        {
            throw new HubException("Forbidden: Customers can only join their own chat sessions.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ChatHubGroups.Session(tenantId, sessionId));
    }

    public async Task LeaveChatSession(Guid sessionId)
    {
        if (TryGetCallerClaims(out var tenantId, out _, out _, out _))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatHubGroups.Session(tenantId, sessionId));
        }
    }

    public async Task SendTypingIndicator(Guid sessionId, bool isTyping)
    {
        if (!TryGetCallerClaims(out var tenantId, out var userId, out var displayName, out _))
        {
            return;
        }

        var indicator = new ChatTypingDto(sessionId, userId, displayName, isTyping);
        await Clients.Group(ChatHubGroups.Session(tenantId, sessionId)).ChatTypingIndicator(indicator);
    }

    private void EnsureTenantContext(Guid tenantId)
    {
        var current = _tenantContextAccessor.TenantContext;
        if (current == null || !current.IsResolved || current.TenantId != tenantId)
        {
            _tenantContextAccessor.TenantContext = new TenantContext
            {
                TenantId = tenantId,
                Status = TenantStatus.Active
            };
        }
    }

    private bool TryGetCallerClaims(
        out Guid tenantId,
        out Guid userId,
        out string displayName,
        out bool isStaff)
    {
        tenantId = Guid.Empty;
        userId = Guid.Empty;
        displayName = "User";
        isStaff = false;

        var user = Context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var tenantIdClaim = user.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantIdClaim, out tenantId) || tenantId == Guid.Empty)
        {
            return false;
        }

        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (!Guid.TryParse(userIdClaim, out userId) || userId == Guid.Empty)
        {
            return false;
        }

        var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
        isStaff = string.Equals(role, IdentityRoles.Agent, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(role, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        displayName = user.FindFirstValue("name")
                      ?? user.FindFirstValue(ClaimTypes.Name)
                      ?? user.FindFirstValue(ClaimTypes.Email)
                      ?? user.FindFirstValue("email")
                      ?? (isStaff ? "Agent" : "Customer");

        return true;
    }
}
