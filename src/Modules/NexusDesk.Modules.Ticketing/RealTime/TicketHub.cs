using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Ticketing.RealTime;

[Authorize]
public sealed class TicketHub : Hub<ITicketHubClient>
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ITicketPresenceTracker _presenceTracker;

    public TicketHub(
        TicketingDbContext ticketingDb,
        ITenantContextAccessor tenantContextAccessor,
        ITicketPresenceTracker presenceTracker)
    {
        _ticketingDb = ticketingDb;
        _tenantContextAccessor = tenantContextAccessor;
        _presenceTracker = presenceTracker;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryGetCallerClaims(out var tenantId, out var userId, out _, out var isStaff))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.Tenant(tenantId));
        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.TenantUser(tenantId, userId));

        if (isStaff)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.TenantStaff(tenantId));
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinTicketRoom(Guid ticketId)
    {
        if (!TryGetCallerClaims(out var tenantId, out var userId, out var displayName, out var isStaff))
        {
            throw new HubException("Unauthorized.");
        }

        EnsureTenantContext(tenantId);

        var ticket = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.Id == ticketId && t.TenantId == tenantId)
            .Select(t => new { t.Id, t.CustomerId })
            .FirstOrDefaultAsync(Context.ConnectionAborted);

        if (ticket == null || (!isStaff && ticket.CustomerId != userId))
        {
            throw new HubException("Ticket not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.TicketRoom(tenantId, ticketId));

        if (isStaff)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TicketHubGroups.TicketRoomStaff(tenantId, ticketId));
            _presenceTracker.TrackJoin(tenantId, ticketId, userId, displayName, Context.ConnectionId);
            await BroadcastPresenceAsync(tenantId, ticketId);
        }
    }

    public async Task LeaveTicketRoom(Guid ticketId)
    {
        if (!TryGetCallerClaims(out var tenantId, out _, out _, out var isStaff))
        {
            throw new HubException("Unauthorized.");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketHubGroups.TicketRoom(tenantId, ticketId));

        if (isStaff)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TicketHubGroups.TicketRoomStaff(tenantId, ticketId));
            _presenceTracker.TrackLeave(tenantId, ticketId, Context.ConnectionId);
            await BroadcastPresenceAsync(tenantId, ticketId);
        }
    }

    public async Task SetTypingState(Guid ticketId, bool isTyping)
    {
        if (!TryGetCallerClaims(out var tenantId, out _, out _, out var isStaff))
        {
            throw new HubException("Unauthorized.");
        }

        if (!isStaff)
        {
            throw new HubException("Only staff members can broadcast typing presence.");
        }

        if (_presenceTracker.SetTyping(tenantId, ticketId, Context.ConnectionId, isTyping))
        {
            await BroadcastPresenceAsync(tenantId, ticketId);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var affectedRooms = _presenceTracker.RemoveConnection(Context.ConnectionId);
        foreach (var (tenantId, ticketId) in affectedRooms)
        {
            await BroadcastPresenceAsync(tenantId, ticketId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastPresenceAsync(Guid tenantId, Guid ticketId)
    {
        var activeAgents = _presenceTracker.GetActiveAgents(tenantId, ticketId);
        var notification = new TicketPresenceNotification(
            ticketId,
            tenantId,
            activeAgents,
            DateTimeOffset.UtcNow);

        return Clients.Group(TicketHubGroups.TicketRoomStaff(tenantId, ticketId))
            .AgentPresenceUpdated(notification);
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
        displayName = "Agent";
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
                      ?? "Agent";

        return true;
    }
}
