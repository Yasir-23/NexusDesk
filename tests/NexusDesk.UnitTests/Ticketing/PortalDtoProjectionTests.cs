using FluentAssertions;
using NexusDesk.Modules.Ticketing.Application;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using Xunit;

namespace NexusDesk.UnitTests.Ticketing;

public class PortalDtoProjectionTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _agentId = Guid.NewGuid();

    [Fact]
    public void CustomerView_ShouldStripInternalNotes_AndBlankActivityLogs()
    {
        // 1. Arrange a ticket with customer public replies, agent public replies, and internal notes
        var ticket = Ticket.Create(
            _tenantId,
            "ND-20001",
            "Payment failed",
            "Credit card was declined",
            TicketPriority.High,
            TicketChannel.WebPortal,
            _customerId,
            ["billing"],
            DateTimeOffset.UtcNow).Value;

        // Customer replies publicly
        ticket.AddComment("Please help, my card is valid", CommentType.PublicReply, _customerId, isCustomer: true);

        // Agent assigns and changes status (generates activity logs)
        ticket.AssignToAgent(_agentId, _agentId, DateTimeOffset.UtcNow);

        // Agent adds an internal note (MUST NEVER BE VISIBLE TO CUSTOMER)
        ticket.AddComment("Customer account flagged for manual fraud review. Do not refund yet.", CommentType.InternalNote, _agentId, isCustomer: false);

        // Agent replies publicly
        ticket.AddComment("We are currently investigating this issue with our payment processor.", CommentType.PublicReply, _agentId, isCustomer: false);

        // 2. Act: project for customer view (isCustomer = true)
        var customerView = ticket.ToDetailsResponse(isCustomer: true);

        // 3. Assert
        customerView.Comments.Should().HaveCount(2);
        customerView.Comments.Should().OnlyContain(c => c.Type == CommentType.PublicReply);
        customerView.Comments.Should().NotContain(c => c.Type == CommentType.InternalNote);
        customerView.Comments.Should().NotContain(c => c.Body.Contains("fraud review"));

        // Activity logs must be strictly empty in customer view
        customerView.ActivityLogs.Should().BeEmpty();
    }

    [Fact]
    public void AgentView_ShouldPreserveInternalNotes_AndIncludeActivityLogs()
    {
        // 1. Arrange
        var ticket = Ticket.Create(
            _tenantId,
            "ND-20002",
            "Account locked",
            "Too many failed login attempts",
            TicketPriority.Medium,
            TicketChannel.WebPortal,
            _customerId,
            ["security"],
            DateTimeOffset.UtcNow).Value;

        ticket.AddComment("Help unlock please", CommentType.PublicReply, _customerId, isCustomer: true);
        ticket.AssignToAgent(_agentId, _agentId, DateTimeOffset.UtcNow);
        ticket.AddComment("IP 192.168.1.1 tried 15 passwords", CommentType.InternalNote, _agentId, isCustomer: false);
        ticket.AddComment("Password reset email sent", CommentType.PublicReply, _agentId, isCustomer: false);

        // 2. Act: project for agent view (isCustomer = false)
        var agentView = ticket.ToDetailsResponse(isCustomer: false);

        // 3. Assert
        agentView.Comments.Should().HaveCount(3);
        agentView.Comments.Should().Contain(c => c.Type == CommentType.InternalNote && c.Body.Contains("192.168.1.1"));
        agentView.ActivityLogs.Should().NotBeEmpty();
    }
}
