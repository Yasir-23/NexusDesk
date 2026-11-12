using System.Collections.Concurrent;

namespace NexusDesk.Modules.Ticketing.RealTime;

/// <summary>
/// Thread-safe in-memory presence tracker for active staff viewers and typing indicators per ticket room.
/// </summary>
public sealed class InMemoryTicketPresenceTracker : ITicketPresenceTracker
{
    private sealed record PresenceEntry(
        Guid UserId,
        string DisplayName,
        string ConnectionId,
        bool IsTyping,
        DateTimeOffset LastActivityUtc);

    private readonly ConcurrentDictionary<(Guid TenantId, Guid TicketId), ConcurrentDictionary<string, PresenceEntry>> _roomConnections = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<(Guid TenantId, Guid TicketId), byte>> _connectionRooms = new();

    public void TrackJoin(Guid tenantId, Guid ticketId, Guid userId, string displayName, string connectionId)
    {
        if (tenantId == Guid.Empty || ticketId == Guid.Empty || userId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        var roomKey = (tenantId, ticketId);
        var normalizedName = string.IsNullOrWhiteSpace(displayName) ? "Agent" : displayName.Trim();
        var now = DateTimeOffset.UtcNow;

        var room = _roomConnections.GetOrAdd(roomKey, _ => new ConcurrentDictionary<string, PresenceEntry>(StringComparer.Ordinal));
        room[connectionId] = new PresenceEntry(userId, normalizedName, connectionId, false, now);

        var roomsForConn = _connectionRooms.GetOrAdd(connectionId, _ => new ConcurrentDictionary<(Guid, Guid), byte>());
        roomsForConn[roomKey] = 0;
    }

    public void TrackLeave(Guid tenantId, Guid ticketId, string connectionId)
    {
        if (tenantId == Guid.Empty || ticketId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        var roomKey = (tenantId, ticketId);

        if (_roomConnections.TryGetValue(roomKey, out var room))
        {
            room.TryRemove(connectionId, out _);
            if (room.IsEmpty)
            {
                _roomConnections.TryRemove(new KeyValuePair<(Guid, Guid), ConcurrentDictionary<string, PresenceEntry>>(roomKey, room));
            }
        }

        if (_connectionRooms.TryGetValue(connectionId, out var roomsForConn))
        {
            roomsForConn.TryRemove(roomKey, out _);
            if (roomsForConn.IsEmpty)
            {
                _connectionRooms.TryRemove(new KeyValuePair<string, ConcurrentDictionary<(Guid, Guid), byte>>(connectionId, roomsForConn));
            }
        }
    }

    public bool SetTyping(Guid tenantId, Guid ticketId, string connectionId, bool isTyping)
    {
        if (tenantId == Guid.Empty || ticketId == Guid.Empty || string.IsNullOrWhiteSpace(connectionId))
        {
            return false;
        }

        var roomKey = (tenantId, ticketId);
        if (!_roomConnections.TryGetValue(roomKey, out var room))
        {
            return false;
        }

        while (room.TryGetValue(connectionId, out var existing))
        {
            var updated = existing with
            {
                IsTyping = isTyping,
                LastActivityUtc = DateTimeOffset.UtcNow
            };

            if (room.TryUpdate(connectionId, updated, existing))
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<(Guid TenantId, Guid TicketId)> RemoveConnection(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return [];
        }

        if (!_connectionRooms.TryRemove(connectionId, out var roomsForConn))
        {
            return [];
        }

        var affectedRooms = new List<(Guid TenantId, Guid TicketId)>(roomsForConn.Count);

        foreach (var roomKey in roomsForConn.Keys)
        {
            if (_roomConnections.TryGetValue(roomKey, out var room) && room.TryRemove(connectionId, out _))
            {
                affectedRooms.Add(roomKey);

                if (room.IsEmpty)
                {
                    _roomConnections.TryRemove(new KeyValuePair<(Guid, Guid), ConcurrentDictionary<string, PresenceEntry>>(roomKey, room));
                }
            }
        }

        return affectedRooms;
    }

    public IReadOnlyList<TicketPresenceAgentDto> GetActiveAgents(Guid tenantId, Guid ticketId)
    {
        if (tenantId == Guid.Empty || ticketId == Guid.Empty)
        {
            return [];
        }

        if (!_roomConnections.TryGetValue((tenantId, ticketId), out var room) || room.IsEmpty)
        {
            return [];
        }

        var snapshot = room.Values.ToArray();
        if (snapshot.Length == 0)
        {
            return [];
        }

        return snapshot
            .GroupBy(e => e.UserId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.LastActivityUtc).First();
                return new TicketPresenceAgentDto(
                    g.Key,
                    latest.DisplayName,
                    g.Any(e => e.IsTyping),
                    latest.LastActivityUtc);
            })
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.UserId)
            .ToArray();
    }
}
