using FluentAssertions;
using NexusDesk.Modules.Automation.Application;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.Modules.Ticketing.Domain.ValueObjects;
using NexusDesk.SharedKernel.Automation;
using Xunit;

namespace NexusDesk.UnitTests.Automation;

public class SlaPolicyAndRuleTests
{
    [Fact]
    public void SlaPolicy_Create_ShouldNormalizePriority_AndRejectInvalidInputs()
    {
        var tenantId = Guid.NewGuid();

        var valid = SlaPolicy.Create(tenantId, "urgent", 30, 120);
        valid.IsSuccess.Should().BeTrue();
        valid.Value.Priority.Should().Be("Urgent");
        valid.Value.FirstResponseTargetMinutes.Should().Be(30);
        valid.Value.ResolutionTargetMinutes.Should().Be(120);

        SlaPolicy.Create(tenantId, "Critical", 30, 120).IsFailure.Should().BeTrue();
        SlaPolicy.Create(tenantId, "High", 0, 120).IsFailure.Should().BeTrue();
        SlaPolicy.Create(tenantId, "High", 30, -5).IsFailure.Should().BeTrue();
        SlaPolicy.Create(Guid.Empty, "High", 30, 120).IsFailure.Should().BeTrue();

        var updateInvalid = valid.Value.UpdateTargets(0, 60);
        updateInvalid.IsFailure.Should().BeTrue();

        var updateValid = valid.Value.UpdateTargets(15, 90);
        updateValid.IsSuccess.Should().BeTrue();
        valid.Value.FirstResponseTargetMinutes.Should().Be(15);
        valid.Value.ResolutionTargetMinutes.Should().Be(90);
    }

    [Fact]
    public void Ticket_CreateAndChangePriority_ShouldUseCustomSlaTargets_WhenProvided()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        var customTargets = new SlaTargets(now.AddMinutes(10), now.AddMinutes(45));

        var ticketResult = Ticket.Create(
            tenantId,
            "TICK-1001",
            "Custom SLA test",
            "Testing custom SLA policy targets",
            TicketPriority.High,
            TicketChannel.WebPortal,
            customerId,
            utcNow: now,
            customSlaTargets: customTargets);

        ticketResult.IsSuccess.Should().BeTrue();
        var ticket = ticketResult.Value;
        ticket.FirstResponseDueUtc.Should().Be(now.AddMinutes(10));
        ticket.ResolutionDueUtc.Should().Be(now.AddMinutes(45));

        var escalatedTargets = new SlaTargets(now.AddMinutes(5), now.AddMinutes(20));
        var changeResult = ticket.ChangePriority(
            TicketPriority.Urgent,
            Guid.NewGuid(),
            utcNow: now.AddMinutes(1),
            customSlaTargets: escalatedTargets);

        changeResult.IsSuccess.Should().BeTrue();
        ticket.FirstResponseDueUtc.Should().Be(now.AddMinutes(5));
        ticket.ResolutionDueUtc.Should().Be(now.AddMinutes(20));
    }

    [Fact]
    public void Ticket_MarkSlaBreached_ShouldSetFlagsRaiseEventAndBeIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

        var ticket = Ticket.Create(
            tenantId,
            "TICK-1002",
            "Breach test",
            "Testing SLA breach domain behavior",
            TicketPriority.Urgent,
            TicketChannel.Api,
            customerId,
            utcNow: createdAt).Value;

        ticket.ClearDomainEvents();

        var breachedAt = createdAt.AddHours(2);
        var breachResult = ticket.MarkSlaBreached("FirstResponse", breachedAt);

        breachResult.IsSuccess.Should().BeTrue();
        ticket.IsSlaBreached.Should().BeTrue();
        ticket.SlaBreachedAtUtc.Should().Be(breachedAt);
        ticket.ActivityLogs.Should().ContainSingle(a => a.Action == "SlaBreached" && a.NewValue == "FirstResponse");
        ticket.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TicketSlaBreachedDomainEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                TicketId = ticket.Id,
                TenantId = tenantId,
                TicketNumber = "TICK-1002",
                Priority = "Urgent",
                BreachType = "FirstResponse",
                BreachedAtUtc = breachedAt
            });

        // Idempotent second call must not emit duplicate domain event
        ticket.ClearDomainEvents();
        var secondResult = ticket.MarkSlaBreached("Resolution", breachedAt.AddHours(3));
        secondResult.IsSuccess.Should().BeTrue();
        ticket.SlaBreachedAtUtc.Should().Be(breachedAt);
        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Ticket_MarkSlaBreached_ShouldFail_WhenResolvedOrClosed()
    {
        var ticket = Ticket.Create(
            Guid.NewGuid(),
            "TICK-1003",
            "Resolved ticket",
            "Already resolved",
            TicketPriority.Low,
            TicketChannel.Email,
            Guid.NewGuid()).Value;

        ticket.Resolve("Fixed", Guid.NewGuid()).IsSuccess.Should().BeTrue();
        ticket.MarkSlaBreached("FirstResponse", DateTimeOffset.UtcNow).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AutomationRule_CreateAndToggle_ShouldValidateInputs()
    {
        var tenantId = Guid.NewGuid();

        var ruleResult = AutomationRule.Create(
            tenantId,
            "Escalate Urgent SLA Breach",
            "TicketSlaBreached",
            """{"priority":"High"}""",
            "EscalatePriority",
            """{"priority":"Urgent"}""");

        ruleResult.IsSuccess.Should().BeTrue();
        var rule = ruleResult.Value;
        rule.IsEnabled.Should().BeTrue();

        rule.Toggle().Should().BeFalse();
        rule.IsEnabled.Should().BeFalse();

        rule.SetEnabled(true);
        rule.IsEnabled.Should().BeTrue();

        AutomationRule.Create(tenantId, "", "TicketCreated", "{}", "AddInternalNote", "{}").IsFailure.Should().BeTrue();
        AutomationRule.Create(tenantId, "Bad Trigger", "UnknownEvent", "{}", "AddInternalNote", "{}").IsFailure.Should().BeTrue();
        AutomationRule.Create(tenantId, "Bad Action", "TicketCreated", "{}", "DeleteTicket", "{}").IsFailure.Should().BeTrue();
        AutomationRule.Create(tenantId, "Bad Json", "TicketCreated", "not-json", "AddInternalNote", "{}").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AutomationRuleEvaluator_MatchesCondition_ShouldEvaluateJsonConditions()
    {
        var context = new TicketAutomationEventContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TICK-2001",
            "TicketSlaBreached",
            "High",
            BreachType: "FirstResponse");

        AutomationRuleEvaluator.MatchesCondition("{}", context).Should().BeTrue();
        AutomationRuleEvaluator.MatchesCondition("""{"priority":"high"}""", context).Should().BeTrue();
        AutomationRuleEvaluator.MatchesCondition("""{"priority":"High","breachType":"FirstResponse"}""", context).Should().BeTrue();
        AutomationRuleEvaluator.MatchesCondition("""{"priority":"Urgent"}""", context).Should().BeFalse();
        AutomationRuleEvaluator.MatchesCondition("""{"breachType":"Resolution"}""", context).Should().BeFalse();
        AutomationRuleEvaluator.MatchesCondition("invalid-json", context).Should().BeFalse();
    }
}
