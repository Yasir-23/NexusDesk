using FluentAssertions;
using NexusDesk.Modules.Chat.Domain;
using Xunit;

namespace NexusDesk.UnitTests.Chat;

public class ChatSessionStateMachineTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _agentId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldInitializeInWaitingStatus_WithCustomerAndSubject()
    {
        var result = ChatSession.Create(_tenantId, _customerId, "Need help with billing");

        result.IsSuccess.Should().BeTrue();
        var session = result.Value;
        session.TenantId.Should().Be(_tenantId);
        session.CustomerId.Should().Be(_customerId);
        session.Subject.Should().Be("Need help with billing");
        session.Status.Should().Be(ChatSessionStatus.Waiting);
        session.AssignedAgentId.Should().BeNull();
        session.ClosedAtUtc.Should().BeNull();
        session.ConvertedTicketId.Should().BeNull();
        session.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidSubject_ShouldFail(string? invalidSubject)
    {
        var result = ChatSession.Create(_tenantId, _customerId, invalidSubject!);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ChatSession.EmptySubject");
    }

    [Fact]
    public void Claim_OnWaitingSession_ShouldTransitionToActive_AndAssignAgent()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;

        var claimResult = session.Claim(_agentId);

        claimResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(ChatSessionStatus.Active);
        session.AssignedAgentId.Should().Be(_agentId);
    }

    [Fact]
    public void Claim_OnAlreadyActiveSession_ShouldReturnConflict()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;
        session.Claim(_agentId);

        var secondClaim = session.Claim(Guid.NewGuid());

        secondClaim.IsFailure.Should().BeTrue();
        secondClaim.Error.Code.Should().Be("ChatSession.NotWaiting");
    }

    [Fact]
    public void Claim_OnClosedSession_ShouldReturnConflict()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;
        session.Close();

        var claimResult = session.Claim(_agentId);

        claimResult.IsFailure.Should().BeTrue();
        claimResult.Error.Code.Should().Be("ChatSession.AlreadyClosed");
    }

    [Fact]
    public void AddMessage_OnWaitingOrActiveSession_ShouldSucceed()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;

        var msg1Result = session.AddMessage(_customerId, "Customer", "Hello agent!");
        msg1Result.IsSuccess.Should().BeTrue();

        session.Claim(_agentId);
        var msg2Result = session.AddMessage(_agentId, "Agent", "Hello! How can I help you today?");
        msg2Result.IsSuccess.Should().BeTrue();

        session.Messages.Should().HaveCount(2);
        session.Messages[0].Content.Should().Be("Hello agent!");
        session.Messages[0].SenderRole.Should().Be("Customer");
        session.Messages[1].Content.Should().Be("Hello! How can I help you today?");
        session.Messages[1].SenderRole.Should().Be("Agent");
    }

    [Fact]
    public void AddMessage_OnClosedSession_ShouldReturnConflict()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;
        session.Close();

        var result = session.AddMessage(_customerId, "Customer", "Can you hear me?");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ChatSession.AlreadyClosed");
    }

    [Fact]
    public void AddMessage_OnConvertedToTicketSession_ShouldReturnConflict()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;
        var ticketId = Guid.NewGuid();
        session.ConvertToTicket(ticketId);

        var result = session.AddMessage(_customerId, "Customer", "New message");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ChatSession.AlreadyClosed");
    }

    [Fact]
    public void Close_ShouldTransitionToClosed_AndSetClosedAtUtc()
    {
        var now = DateTimeOffset.UtcNow;
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;

        var closeResult = session.Close(now);

        closeResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(ChatSessionStatus.Closed);
        session.ClosedAtUtc.Should().Be(now);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_ShouldReturnConflict()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;
        session.Close();

        var secondClose = session.Close();

        secondClose.IsFailure.Should().BeTrue();
        secondClose.Error.Code.Should().Be("ChatSession.AlreadyClosed");
    }

    [Fact]
    public void ConvertToTicket_ShouldTransitionToConvertedToTicket_AndSetTicketIdAndClosedAt()
    {
        var now = DateTimeOffset.UtcNow;
        var ticketId = Guid.NewGuid();
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;

        var result = session.ConvertToTicket(ticketId, now);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(ChatSessionStatus.ConvertedToTicket);
        session.ConvertedTicketId.Should().Be(ticketId);
        session.ClosedAtUtc.Should().Be(now);
    }

    [Fact]
    public void ConvertToTicket_WhenAlreadyClosed_ShouldReturnConflict()
    {
        var session = ChatSession.Create(_tenantId, _customerId, "Help").Value;
        session.Close();

        var result = session.ConvertToTicket(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ChatSession.AlreadyClosed");
    }
}
