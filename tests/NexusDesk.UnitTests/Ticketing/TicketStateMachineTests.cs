using FluentAssertions;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.SharedKernel.Primitives;
using Xunit;

namespace NexusDesk.UnitTests.Ticketing;

public class TicketStateMachineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly Guid AgentId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    private static Ticket CreateValidTicket(
        TicketPriority priority = TicketPriority.Medium,
        TicketChannel channel = TicketChannel.WebPortal,
        IEnumerable<string>? tags = null)
    {
        var result = Ticket.Create(
            TenantId,
            "TICK-1001",
            "Cannot login to dashboard",
            "Getting 401 on SSO login.",
            priority,
            channel,
            CustomerId,
            tags ?? ["SSO", "urgent-fix", "sso"],
            BaseTime);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public void Create_ValidInputs_ShouldInitializeNewTicketWithSlaTagsAuditAndDomainEvent()
    {
        var ticket = CreateValidTicket(TicketPriority.High);

        ticket.TenantId.Should().Be(TenantId);
        ticket.TicketNumber.Should().Be("TICK-1001");
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.Priority.Should().Be(TicketPriority.High);
        ticket.Channel.Should().Be(TicketChannel.WebPortal);
        ticket.CustomerId.Should().Be(CustomerId);
        ticket.AssignedAgentId.Should().BeNull();
        ticket.FirstResponseDueUtc.Should().Be(BaseTime.AddHours(2));
        ticket.ResolutionDueUtc.Should().Be(BaseTime.AddHours(8));
        ticket.Tags.Select(t => t.Name).Should().BeEquivalentTo(["sso", "urgent-fix"]);
        ticket.ActivityLogs.Should().ContainSingle(l => l.Action == "Created" && l.NewValue == "TICK-1001");
        ticket.DomainEvents.Should().ContainSingle(e => e is TicketCreatedDomainEvent);
    }

    [Theory]
    [InlineData("", "Valid description")]
    [InlineData("   ", "Valid description")]
    [InlineData("Valid title", "")]
    public void Create_MissingTitleOrDescription_ShouldFailValidation(string title, string description)
    {
        var result = Ticket.Create(TenantId, "TICK-1001", title, description, TicketPriority.Low, TicketChannel.Api, CustomerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Create_TitleExceedingMaxLength_ShouldFailValidation()
    {
        var longTitle = new string('A', Ticket.MaxTitleLength + 1);
        var result = Ticket.Create(TenantId, "TICK-1001", longTitle, "Description", TicketPriority.Low, TicketChannel.Email, CustomerId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ticket.TitleTooLong");
    }

    [Fact]
    public void AssignToAgent_FromNew_ShouldTransitionToOpenAndEmitDomainEvents()
    {
        var ticket = CreateValidTicket();
        ticket.ClearDomainEvents();

        var assignResult = ticket.AssignToAgent(AgentId, AdminId, BaseTime.AddMinutes(5));

        assignResult.IsSuccess.Should().BeTrue();
        ticket.AssignedAgentId.Should().Be(AgentId);
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.ActivityLogs.Should().Contain(l => l.Action == "Assigned" && l.NewValue == AgentId.ToString());
        ticket.ActivityLogs.Should().Contain(l => l.Action == "StatusChanged" && l.OldValue == "New" && l.NewValue == "Open");
        ticket.DomainEvents.Should().Contain(e => e is TicketStatusChangedDomainEvent);
        ticket.DomainEvents.Should().Contain(e => e is TicketAssignedDomainEvent);
    }

    [Fact]
    public void AssignToAgent_WhenClosed_ShouldFailValidation()
    {
        var ticket = CreateValidTicket();
        ticket.Close(AdminId, BaseTime.AddMinutes(5)).IsSuccess.Should().BeTrue();

        var result = ticket.AssignToAgent(AgentId, AdminId, BaseTime.AddMinutes(10));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ticket.ClosedCannotAssign");
    }

    [Fact]
    public void ChangePriority_BeforeBreach_ShouldRecalculateBothSlaDeadlines()
    {
        var ticket = CreateValidTicket(TicketPriority.Low); // +24h / +72h

        var result = ticket.ChangePriority(TicketPriority.Urgent, AgentId, BaseTime.AddMinutes(15));

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriority.Urgent);
        ticket.FirstResponseDueUtc.Should().Be(BaseTime.AddHours(1));
        ticket.ResolutionDueUtc.Should().Be(BaseTime.AddHours(4));
        ticket.ActivityLogs.Should().Contain(l => l.Action == "PriorityChanged" && l.OldValue == "Low" && l.NewValue == "Urgent");
    }

    [Fact]
    public void ChangePriority_WhenFirstResponseAlreadyBreached_ShouldPreserveBreachedFirstResponseDeadline()
    {
        var ticket = CreateValidTicket(TicketPriority.Urgent); // FirstResponse +1h, Resolution +4h
        var originalFirstResponseDue = ticket.FirstResponseDueUtc;

        // At +2h, FirstResponse is breached, Resolution (+4h) is NOT breached yet
        var result = ticket.ChangePriority(TicketPriority.Low, AgentId, BaseTime.AddHours(2));

        result.IsSuccess.Should().BeTrue();
        ticket.FirstResponseDueUtc.Should().Be(originalFirstResponseDue);
        ticket.ResolutionDueUtc.Should().Be(BaseTime.AddHours(72));
    }

    [Fact]
    public void AddComment_AgentPublicReply_ShouldSetFirstRespondedAtUtcOnlyOnce()
    {
        var ticket = CreateValidTicket();
        ticket.AssignToAgent(AgentId, AdminId, BaseTime.AddMinutes(5));

        var internalTime = BaseTime.AddMinutes(10);
        var internalNoteResult = ticket.AddComment("Checking logs internally", CommentType.InternalNote, AgentId, isCustomer: false, internalTime);
        internalNoteResult.IsSuccess.Should().BeTrue();
        ticket.FirstRespondedAtUtc.Should().BeNull("Internal notes must not count as first public response.");

        var firstReplyTime = BaseTime.AddMinutes(15);
        var firstReply = ticket.AddComment("We are investigating.", CommentType.PublicReply, AgentId, isCustomer: false, firstReplyTime);
        firstReply.IsSuccess.Should().BeTrue();
        ticket.FirstRespondedAtUtc.Should().Be(firstReplyTime);

        var secondReplyTime = BaseTime.AddMinutes(30);
        var secondReply = ticket.AddComment("Update on fix.", CommentType.PublicReply, AgentId, isCustomer: false, secondReplyTime);
        secondReply.IsSuccess.Should().BeTrue();
        ticket.FirstRespondedAtUtc.Should().Be(firstReplyTime, "FirstRespondedAtUtc must not be overwritten by subsequent replies.");
    }

    [Fact]
    public void AddComment_CustomerReplyWhenPendingCustomer_ShouldTransitionToOpen()
    {
        var ticket = CreateValidTicket();
        ticket.AssignToAgent(AgentId, AdminId, BaseTime.AddMinutes(5));
        ticket.MoveToPendingCustomer(AgentId, BaseTime.AddMinutes(10)).IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.PendingCustomer);

        var customerReply = ticket.AddComment("Here are the screenshots.", CommentType.PublicReply, CustomerId, isCustomer: true, BaseTime.AddMinutes(20));

        customerReply.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.DomainEvents.Should().Contain(e => e is TicketCommentAddedDomainEvent);
    }

    [Fact]
    public void AddComment_CustomerAttemptingInternalNote_ShouldReturnForbidden()
    {
        var ticket = CreateValidTicket();

        var result = ticket.AddComment("Trying internal note", CommentType.InternalNote, CustomerId, isCustomer: true, BaseTime.AddMinutes(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("Ticket.CustomerInternalNoteForbidden");
    }

    [Fact]
    public void GetVisibleComments_ShouldHideInternalNotesFromCustomers()
    {
        var ticket = CreateValidTicket();
        ticket.AddComment("Public response", CommentType.PublicReply, AgentId, isCustomer: false, BaseTime.AddMinutes(5));
        ticket.AddComment("Secret agent note", CommentType.InternalNote, AgentId, isCustomer: false, BaseTime.AddMinutes(6));
        ticket.AddComment("Customer follow-up", CommentType.PublicReply, CustomerId, isCustomer: true, BaseTime.AddMinutes(7));

        var customerComments = ticket.GetVisibleComments(isCustomer: true);
        var agentComments = ticket.GetVisibleComments(isCustomer: false);

        customerComments.Should().HaveCount(2);
        customerComments.Should().NotContain(c => c.Type == CommentType.InternalNote);
        agentComments.Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_RequiresNotes_AndDisallowsClosedOrAlreadyResolvedTickets()
    {
        var ticket = CreateValidTicket();
        ticket.AssignToAgent(AgentId, AdminId, BaseTime.AddMinutes(5));

        var missingNotes = ticket.Resolve("   ", AgentId, BaseTime.AddMinutes(10));
        missingNotes.IsFailure.Should().BeTrue();
        missingNotes.Error.Code.Should().Be("Ticket.ResolutionNotesRequired");

        var resolveOk = ticket.Resolve("Reset SSO certificate.", AgentId, BaseTime.AddMinutes(15));
        resolveOk.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Resolved);
        ticket.ResolvedAtUtc.Should().Be(BaseTime.AddMinutes(15));
        ticket.ResolutionNotes.Should().Be("Reset SSO certificate.");

        var resolveAgain = ticket.Resolve("Duplicate resolve", AgentId, BaseTime.AddMinutes(20));
        resolveAgain.IsFailure.Should().BeTrue();
        resolveAgain.Error.Code.Should().Be("Ticket.AlreadyResolved");

        ticket.Close(AgentId, BaseTime.AddMinutes(25)).IsSuccess.Should().BeTrue();
        var resolveClosed = ticket.Resolve("Trying to resolve closed", AgentId, BaseTime.AddMinutes(30));
        resolveClosed.IsFailure.Should().BeTrue();
        resolveClosed.Error.Code.Should().Be("Ticket.CannotResolveClosed");
    }

    [Fact]
    public void Close_And_Reopen_ShouldEnforceLifecycleInvariants()
    {
        var ticket = CreateValidTicket();
        ticket.AssignToAgent(AgentId, AdminId, BaseTime.AddMinutes(5));

        // Cannot reopen an Open ticket
        var invalidReopen = ticket.Reopen("Not closed yet", AgentId, BaseTime.AddMinutes(6));
        invalidReopen.IsFailure.Should().BeTrue();
        invalidReopen.Error.Code.Should().Be("Ticket.InvalidReopen");

        ticket.Resolve("Fixed issue", AgentId, BaseTime.AddMinutes(10)).IsSuccess.Should().BeTrue();
        ticket.Close(AgentId, BaseTime.AddMinutes(15)).IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Closed);
        ticket.ClosedAtUtc.Should().Be(BaseTime.AddMinutes(15));

        // Cannot close twice
        var closeTwice = ticket.Close(AgentId, BaseTime.AddMinutes(16));
        closeTwice.IsFailure.Should().BeTrue();
        closeTwice.Error.Code.Should().Be("Ticket.AlreadyClosed");

        // Reopen requires reason
        var reopenNoReason = ticket.Reopen("", CustomerId, BaseTime.AddMinutes(20));
        reopenNoReason.IsFailure.Should().BeTrue();
        reopenNoReason.Error.Code.Should().Be("Ticket.ReopenReasonRequired");

        // Valid reopen from Closed -> Open
        var reopenOk = ticket.Reopen("Issue recurred after deployment", AgentId, BaseTime.AddMinutes(25));
        reopenOk.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.ResolvedAtUtc.Should().BeNull();
        ticket.ClosedAtUtc.Should().BeNull();
    }

    [Fact]
    public void MoveToPendingAndOpen_FromResolved_ShouldRequireExplicitReopen()
    {
        var ticket = CreateValidTicket();
        ticket.Resolve("Resolved", AgentId, BaseTime.AddMinutes(5)).IsSuccess.Should().BeTrue();

        ticket.MoveToOpen(AgentId).IsFailure.Should().BeTrue();
        ticket.MoveToPendingCustomer(AgentId).IsFailure.Should().BeTrue();
        ticket.MoveToPendingInternal(AgentId).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ChangePriority_WhenChanged_ShouldRaiseTicketPriorityChangedDomainEvent()
    {
        var ticket = CreateValidTicket(TicketPriority.Medium);
        ticket.ClearDomainEvents();

        // Same priority should be a no-op without raising an event
        var sameResult = ticket.ChangePriority(TicketPriority.Medium, AgentId, BaseTime.AddMinutes(5));
        sameResult.IsSuccess.Should().BeTrue();
        ticket.DomainEvents.Should().BeEmpty();

        // Different priority should raise TicketPriorityChangedDomainEvent
        var changeResult = ticket.ChangePriority(TicketPriority.Urgent, AgentId, BaseTime.AddMinutes(10));
        changeResult.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriority.Urgent);

        var domainEvent = ticket.DomainEvents.Should().ContainSingle(e => e is TicketPriorityChangedDomainEvent)
            .Which.Should().BeOfType<TicketPriorityChangedDomainEvent>().Subject;

        domainEvent.TicketId.Should().Be(ticket.Id);
        domainEvent.TenantId.Should().Be(TenantId);
        domainEvent.TicketNumber.Should().Be(ticket.TicketNumber);
        domainEvent.OldPriority.Should().Be(TicketPriority.Medium);
        domainEvent.NewPriority.Should().Be(TicketPriority.Urgent);
        domainEvent.PerformedByUserId.Should().Be(AgentId);
    }
}
