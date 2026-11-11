using FluentAssertions;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Domain.ValueObjects;
using Xunit;

namespace NexusDesk.UnitTests.Ticketing;

public class SlaPolicyMatrixTests
{
    [Theory]
    [InlineData(TicketPriority.Urgent, 1, 4)]
    [InlineData(TicketPriority.High, 2, 8)]
    [InlineData(TicketPriority.Medium, 8, 24)]
    [InlineData(TicketPriority.Low, 24, 72)]
    public void CalculateTargets_ShouldComputeCorrectSlaDeadlinesForEachPriority(
        TicketPriority priority,
        int expectedFirstResponseHours,
        int expectedResolutionHours)
    {
        var createdAt = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

        var targets = SlaPolicyMatrix.CalculateTargets(priority, createdAt);

        targets.FirstResponseDueUtc.Should().Be(createdAt.AddHours(expectedFirstResponseHours));
        targets.ResolutionDueUtc.Should().Be(createdAt.AddHours(expectedResolutionHours));
    }

    [Fact]
    public void CalculateTargets_InvalidPriority_ShouldThrowArgumentOutOfRangeException()
    {
        var act = () => SlaPolicyMatrix.CalculateTargets((TicketPriority)999, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
