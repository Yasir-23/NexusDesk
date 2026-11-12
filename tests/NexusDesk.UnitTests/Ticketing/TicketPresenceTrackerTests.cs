using FluentAssertions;
using NexusDesk.Modules.Ticketing.RealTime;
using Xunit;

namespace NexusDesk.UnitTests.Ticketing;

public class TicketPresenceTrackerTests
{
    [Fact]
    public void TrackJoin_And_TrackLeave_ShouldUpdateActiveAgents()
    {
        var tracker = new InMemoryTicketPresenceTracker();
        var tenantId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        tracker.TrackJoin(tenantId, ticketId, agentId, "Alice Agent", "conn-1");

        var active = tracker.GetActiveAgents(tenantId, ticketId);
        active.Should().ContainSingle();
        active[0].UserId.Should().Be(agentId);
        active[0].DisplayName.Should().Be("Alice Agent");
        active[0].IsTyping.Should().BeFalse();

        tracker.TrackLeave(tenantId, ticketId, "conn-1");

        tracker.GetActiveAgents(tenantId, ticketId).Should().BeEmpty();
    }

    [Fact]
    public void SetTyping_ShouldToggleTypingStateForJoinedConnection()
    {
        var tracker = new InMemoryTicketPresenceTracker();
        var tenantId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        tracker.SetTyping(tenantId, ticketId, "missing-conn", true).Should().BeFalse();

        tracker.TrackJoin(tenantId, ticketId, agentId, "Bob Agent", "conn-1");
        tracker.SetTyping(tenantId, ticketId, "conn-1", true).Should().BeTrue();

        var typingAgents = tracker.GetActiveAgents(tenantId, ticketId);
        typingAgents.Should().ContainSingle();
        typingAgents[0].IsTyping.Should().BeTrue();

        tracker.SetTyping(tenantId, ticketId, "conn-1", false).Should().BeTrue();
        tracker.GetActiveAgents(tenantId, ticketId)[0].IsTyping.Should().BeFalse();
    }

    [Fact]
    public void MultiTabDeduplication_ShouldDeduplicateByUserIdAndAggregateTyping()
    {
        var tracker = new InMemoryTicketPresenceTracker();
        var tenantId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        tracker.TrackJoin(tenantId, ticketId, agentId, "Alice Agent", "tab-1");
        tracker.TrackJoin(tenantId, ticketId, agentId, "Alice Agent", "tab-2");

        tracker.GetActiveAgents(tenantId, ticketId).Should().ContainSingle();

        tracker.SetTyping(tenantId, ticketId, "tab-2", true).Should().BeTrue();
        var activeWhileTyping = tracker.GetActiveAgents(tenantId, ticketId);
        activeWhileTyping.Should().ContainSingle();
        activeWhileTyping[0].IsTyping.Should().BeTrue();

        // Closing first tab should keep agent active and still typing via tab-2
        tracker.TrackLeave(tenantId, ticketId, "tab-1");
        var activeAfterTab1Closed = tracker.GetActiveAgents(tenantId, ticketId);
        activeAfterTab1Closed.Should().ContainSingle();
        activeAfterTab1Closed[0].IsTyping.Should().BeTrue();

        // Closing second tab removes agent completely
        tracker.TrackLeave(tenantId, ticketId, "tab-2");
        tracker.GetActiveAgents(tenantId, ticketId).Should().BeEmpty();
    }

    [Fact]
    public void RemoveConnection_ShouldCleanUpAllRoomsForConnection()
    {
        var tracker = new InMemoryTicketPresenceTracker();
        var tenantId = Guid.NewGuid();
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        tracker.TrackJoin(tenantId, ticket1, agentId, "Alice", "conn-1");
        tracker.TrackJoin(tenantId, ticket2, agentId, "Alice", "conn-1");

        var affected = tracker.RemoveConnection("conn-1");
        affected.Should().BeEquivalentTo(new[] { (tenantId, ticket1), (tenantId, ticket2) });

        tracker.GetActiveAgents(tenantId, ticket1).Should().BeEmpty();
        tracker.GetActiveAgents(tenantId, ticket2).Should().BeEmpty();
    }

    [Fact]
    public void TenantIsolation_ShouldSeparatePresenceAcrossTenants()
    {
        var tracker = new InMemoryTicketPresenceTracker();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedTicketId = Guid.NewGuid();
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();

        tracker.TrackJoin(tenantA, sharedTicketId, agentA, "Agent A", "conn-a");
        tracker.TrackJoin(tenantB, sharedTicketId, agentB, "Agent B", "conn-b");

        var activeA = tracker.GetActiveAgents(tenantA, sharedTicketId);
        activeA.Should().ContainSingle();
        activeA[0].UserId.Should().Be(agentA);

        var activeB = tracker.GetActiveAgents(tenantB, sharedTicketId);
        activeB.Should().ContainSingle();
        activeB[0].UserId.Should().Be(agentB);
    }
}
