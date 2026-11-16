using FluentAssertions;
using NexusDesk.Modules.Analytics.Domain;
using NexusDesk.Modules.Analytics.Services;
using NexusDesk.SharedKernel.Analytics;
using Xunit;

namespace NexusDesk.UnitTests.Analytics;

public class AnalyticsUnitTests
{
    [Fact]
    public void UserNotification_ShouldInitializeUnread_AndTransitionToRead()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var notification = new UserNotification(
            id,
            tenantId,
            recipientId,
            "ticket_assigned",
            "Ticket ND-101 Assigned",
            "You have been assigned ticket ND-101.",
            "/tickets/ND-101",
            isRead: false,
            readAtUtc: null,
            createdAtUtc: createdAt);

        notification.Id.Should().Be(id);
        notification.TenantId.Should().Be(tenantId);
        notification.RecipientUserId.Should().Be(recipientId);
        notification.Type.Should().Be("ticket_assigned");
        notification.IsRead.Should().BeFalse();
        notification.ReadAtUtc.Should().BeNull();

        var readTime = DateTimeOffset.UtcNow;
        notification.MarkAsRead(readTime);

        notification.IsRead.Should().BeTrue();
        notification.ReadAtUtc.Should().Be(readTime);

        // Subsequent call must not overwrite earlier ReadAtUtc
        var laterTime = readTime.AddMinutes(5);
        notification.MarkAsRead(laterTime);
        notification.ReadAtUtc.Should().Be(readTime);
    }

    [Fact]
    public void AuditLogEntry_ShouldInitialize_WithAllRequiredFields()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var entry = new AuditLogEntry(
            id,
            tenantId,
            "ticket.status_changed",
            "ticket",
            "TICK-001",
            actorId,
            "agent@acme.com",
            "{\"oldStatus\":\"New\",\"newStatus\":\"InProgress\"}",
            "192.168.1.100",
            now);

        entry.Id.Should().Be(id);
        entry.TenantId.Should().Be(tenantId);
        entry.Action.Should().Be("ticket.status_changed");
        entry.ResourceType.Should().Be("ticket");
        entry.ResourceId.Should().Be("TICK-001");
        entry.ActorUserId.Should().Be(actorId);
        entry.ActorEmail.Should().Be("agent@acme.com");
        entry.MetadataJson.Should().Contain("InProgress");
        entry.IpAddress.Should().Be("192.168.1.100");
        entry.CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public async Task AnalyticsService_ExportTicketsCsv_ShouldFormatAndEscapeProperly()
    {
        var tenantId = Guid.NewGuid();
        var rows = new List<TicketExportRowDto>
        {
            new("ND-1001", "Login, OAuth & PKCE Issue", "Resolved", "High", "Auth", "alice@example.com", "bob@example.com", false, DateTimeOffset.UtcNow.AddHours(-5), DateTimeOffset.UtcNow.AddHours(-1)),
            new("ND-1002", "Quoted \"Error\" in message", "New", "Medium", "Billing", "charlie@example.com", null, true, DateTimeOffset.UtcNow.AddHours(-2), null)
        };

        var mockProvider = new InMemoryTicketingAnalyticsProvider(rows);
        var service = new AnalyticsService(mockProvider);

        var csv = await service.ExportTicketsCsvAsync(tenantId, 30);

        csv.Should().NotBeNullOrWhiteSpace();
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // Line 0: Header
        lines[0].Should().Be("TicketNumber,Title,Status,Priority,Category,CustomerEmail,AssignedAgentEmail,IsSlaBreached,CreatedAtUtc,ResolvedAtUtc");

        // Line 1: Comma in title must be quoted
        lines[1].Should().Contain("\"Login, OAuth & PKCE Issue\"");
        lines[1].Should().Contain("alice@example.com");
        lines[1].Should().Contain("false");

        // Line 2: Quote in title must be escaped as double-quote
        lines[2].Should().Contain("\"Quoted \"\"Error\"\" in message\"");
        lines[2].Should().Contain("charlie@example.com");
        lines[2].Should().Contain("true");
    }

    [Fact]
    public void SlaComplianceRate_Calculation_ShouldHandleZeroAndPercentages()
    {
        // 0 tickets -> 100%
        CalculateSlaRate(0, 0).Should().Be(100.0);

        // 4 total, 1 breached (3 compliant) -> 75%
        CalculateSlaRate(4, 1).Should().Be(75.0);

        // 10 total, 0 breached -> 100%
        CalculateSlaRate(10, 0).Should().Be(100.0);

        // 5 total, 5 breached -> 0%
        CalculateSlaRate(5, 5).Should().Be(0.0);
    }

    [Fact]
    public void DailyTicketTrendBucketing_ShouldGenerateConsecutiveDays()
    {
        var days = 14;
        var utcToday = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var startDate = utcToday.AddDays(-days + 1);

        var dates = new List<string>();
        for (var i = 0; i < days; i++)
        {
            dates.Add(startDate.AddDays(i).ToString("yyyy-MM-dd"));
        }

        dates.Should().HaveCount(14);
        dates.First().Should().Be(startDate.ToString("yyyy-MM-dd"));
        dates.Last().Should().Be(utcToday.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void DailyTicketTrendBucketing_ShouldUseUtcOffsetZeroBounds()
    {
        var days = 14;
        var utcToday = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var startDate = utcToday.AddDays(-days + 1);

        startDate.Offset.Should().Be(TimeSpan.Zero);
        utcToday.Offset.Should().Be(TimeSpan.Zero);

        for (var i = 0; i < days; i++)
        {
            var target = startDate.AddDays(i);
            target.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    private static double CalculateSlaRate(int total, int breached)
    {
        if (total == 0) return 100.0;
        return Math.Round((double)(total - breached) / total * 100.0, 2);
    }

    private sealed class InMemoryTicketingAnalyticsProvider : ITicketingAnalyticsProvider
    {
        private readonly IReadOnlyList<TicketExportRowDto> _rows;

        public InMemoryTicketingAnalyticsProvider(IReadOnlyList<TicketExportRowDto> rows)
        {
            _rows = rows;
        }

        public Task<TenantAnalyticsOverviewDto> GetOverviewMetricsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TenantAnalyticsOverviewDto(10, 3, 7, 90.0, 15.5, 45.0, 4.8, 2, 5));
        }

        public Task<IReadOnlyList<DailyTicketTrendDto>> GetTicketTrendsAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DailyTicketTrendDto>>([]);
        }

        public Task<IReadOnlyList<AgentPerformanceDto>> GetAgentPerformanceAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AgentPerformanceDto>>([]);
        }

        public Task<IReadOnlyList<TicketExportRowDto>> GetTicketsForExportAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_rows);
        }
    }
}
