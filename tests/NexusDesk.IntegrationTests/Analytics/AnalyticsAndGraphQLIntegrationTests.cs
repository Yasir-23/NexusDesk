using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexusDesk.Modules.Analytics.Contracts;
using NexusDesk.SharedKernel.Analytics;
using Xunit;

namespace NexusDesk.IntegrationTests.Analytics;

[Collection("DatabaseIntegrationCollection")]
public class AnalyticsAndGraphQLIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly Guid AcmeTenantId = Guid.Parse("b7e43bc4-a5c7-4c8f-a470-7277ce9823f4");
    private static readonly Guid BetaTenantId = Guid.Parse("aba0e9fe-90c2-4c6a-8b97-fe7bddf44824");

    public AnalyticsAndGraphQLIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        });
    }

    [Fact]
    public async Task Customer_MustReceiveForbidden_OnAnalyticsAndAuditLogEndpoints()
    {
        var client = _factory.CreateClient();
        var token = AuthTestTokenHelper.CreateCustomerToken(_factory, AcmeTenantId, "acme");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        // 1. Overview
        var overviewRes = await client.GetAsync("/api/v1/analytics/overview?days=30");
        overviewRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Trends
        var trendsRes = await client.GetAsync("/api/v1/analytics/tickets/trends?days=14");
        trendsRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Agent Performance
        var agentsRes = await client.GetAsync("/api/v1/analytics/agents/performance?days=30");
        agentsRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4. CSV Export
        var exportRes = await client.GetAsync("/api/v1/analytics/export/tickets.csv?days=30");
        exportRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Audit Logs
        var auditRes = await client.GetAsync("/api/v1/audit-logs");
        auditRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Agent_CanAccessAnalytics_ButForbiddenOnAuditLogs()
    {
        var client = _factory.CreateClient();
        var token = AuthTestTokenHelper.CreateAgentToken(_factory, AcmeTenantId, "acme");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        // Analytics overview must be 200 OK
        var overviewRes = await client.GetAsync("/api/v1/analytics/overview?days=30");
        overviewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Audit logs must be 403 Forbidden for Agent
        var auditRes = await client.GetAsync("/api/v1/audit-logs");
        auditRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MultiTenantIsolation_AuditLogsAndNotifications_AcmeVsBeta()
    {
        var acmeClient = _factory.CreateClient();
        var acmeToken = AuthTestTokenHelper.CreateTenantAdminToken(_factory, AcmeTenantId, "acme");
        acmeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acmeToken);
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        var betaClient = _factory.CreateClient();
        var betaToken = AuthTestTokenHelper.CreateTenantAdminToken(_factory, BetaTenantId, "beta");
        betaClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", betaToken);
        betaClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

        // 1. Create audit log in Acme
        var acmeAuditPayload = new CreateAuditLogRequest("security.policy_updated", "policy", "pol_001", "{\"setting\":\"mfa_required\"}");
        var acmePostRes = await acmeClient.PostAsJsonAsync("/api/v1/audit-logs", acmeAuditPayload);
        acmePostRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var acmeCreated = await acmePostRes.Content.ReadFromJsonAsync<AuditLogEntryResponse>();
        acmeCreated.Should().NotBeNull();
        acmeCreated!.TenantId.Should().Be(AcmeTenantId);

        // 2. Query Acme audit logs
        var acmeGetRes = await acmeClient.GetAsync("/api/v1/audit-logs");
        acmeGetRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var acmeLogs = await acmeGetRes.Content.ReadFromJsonAsync<PagedAuditLogsResponse>();
        acmeLogs.Should().NotBeNull();
        acmeLogs!.Items.Should().NotBeEmpty();
        acmeLogs.Items.Should().OnlyContain(l => l.TenantId == AcmeTenantId);
        acmeLogs.Items.Should().Contain(l => l.Action == "security.policy_updated");

        // 3. Query Beta audit logs - must NOT contain Acme's entry
        var betaGetRes = await betaClient.GetAsync("/api/v1/audit-logs");
        betaGetRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaLogs = await betaGetRes.Content.ReadFromJsonAsync<PagedAuditLogsResponse>();
        betaLogs.Should().NotBeNull();
        betaLogs!.Items.Should().OnlyContain(l => l.TenantId == BetaTenantId);
        betaLogs.Items.Should().NotContain(l => l.Action == "security.policy_updated");
    }

    [Fact]
    public async Task NotificationLifecycle_ReadSingle_AndReadAll()
    {
        var testUserId = Guid.NewGuid();
        var client = _factory.CreateClient();
        var token = AuthTestTokenHelper.CreateAgentToken(_factory, AcmeTenantId, "acme", userId: testUserId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        // 1. Verify Acme Admin has seeded unread notifications
        var acmeAdminId = Guid.Parse("22222222-2222-2222-2222-222222222221");
        var adminClient = _factory.CreateClient();
        var adminToken = AuthTestTokenHelper.CreateTenantAdminToken(_factory, AcmeTenantId, "acme", userId: acmeAdminId);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        adminClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        var adminNotifRes = await adminClient.GetAsync("/api/v1/notifications");
        adminNotifRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminNotifs = await adminNotifRes.Content.ReadFromJsonAsync<PagedNotificationsResponse>();
        adminNotifs.Should().NotBeNull();
        adminNotifs!.Items.Should().NotBeEmpty();
        adminNotifs.UnreadCount.Should().BeGreaterThanOrEqualTo(2);

        // Mark a single seeded notification read for Acme Admin
        var firstUnread = adminNotifs.Items.First(n => !n.IsRead);
        var readSingleRes = await adminClient.PostAsync($"/api/v1/notifications/{firstUnread.Id}/read", null);
        readSingleRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var markedRead = await readSingleRes.Content.ReadFromJsonAsync<UserNotificationResponse>();
        markedRead.Should().NotBeNull();
        markedRead!.IsRead.Should().BeTrue();
        markedRead.ReadAtUtc.Should().NotBeNull();

        // 2. Admin creates a new notification for testUserId via POST /api/v1/notifications
        var notifRequest = new CreateNotificationRequest(
            testUserId,
            "system_alert",
            "Maintenance Notice",
            "Scheduled maintenance tonight at midnight.",
            "/maintenance");

        var createRes = await adminClient.PostAsJsonAsync("/api/v1/notifications", notifRequest);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. testUserId reads their notifications
        var getRes = await client.GetAsync("/api/v1/notifications");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialNotifs = await getRes.Content.ReadFromJsonAsync<PagedNotificationsResponse>();
        initialNotifs.Should().NotBeNull();
        initialNotifs!.Items.Should().Contain(n => n.Type == "system_alert" && n.Title == "Maintenance Notice");
        initialNotifs.UnreadCount.Should().BeGreaterThanOrEqualTo(1);

        // 4. Mark all read for testUserId
        var readAllRes = await client.PostAsync("/api/v1/notifications/read-all", null);
        readAllRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var readAllResult = await readAllRes.Content.ReadFromJsonAsync<MarkAllNotificationsReadResponse>();
        readAllResult.Should().NotBeNull();
        readAllResult!.MarkedCount.Should().BeGreaterThanOrEqualTo(1);

        // 5. Unread count for testUserId should now be 0
        var afterRes = await client.GetAsync("/api/v1/notifications?unreadOnly=true");
        afterRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterNotifs = await afterRes.Content.ReadFromJsonAsync<PagedNotificationsResponse>();
        afterNotifs.Should().NotBeNull();
        afterNotifs!.UnreadCount.Should().Be(0);
        afterNotifs.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task EndToEndFlow_TicketCreation_QuotaAndAuditLog_AnalyticsOverview_AndGraphQL()
    {
        var adminClient = _factory.CreateClient();
        var adminToken = AuthTestTokenHelper.CreateTenantAdminToken(_factory, AcmeTenantId, "acme");
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        adminClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        // 1. Create a ticket
        var ticketPayload = new
        {
            title = $"M11 Integration Ticket {Guid.NewGuid().ToString()[..8]}",
            description = "Verifying live quota increment and audit log generation",
            priority = 2, // High
            channel = 1   // Email
        };

        var ticketRes = await adminClient.PostAsJsonAsync("/api/v1/tickets", ticketPayload);
        ticketRes.StatusCode.Should().Be(HttpStatusCode.Created);
        using var ticketDoc = JsonDocument.Parse(await ticketRes.Content.ReadAsStringAsync());
        var ticketId = ticketDoc.RootElement.GetProperty("id").GetString();
        ticketId.Should().NotBeNullOrWhiteSpace();

        // 2. Verify audit log entry was automatically created
        var auditRes = await adminClient.GetAsync($"/api/v1/audit-logs?resourceType=ticket");
        auditRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditLogs = await auditRes.Content.ReadFromJsonAsync<PagedAuditLogsResponse>();
        auditLogs.Should().NotBeNull();
        auditLogs!.Items.Should().Contain(a => a.Action == "ticket.created" && a.ResourceId == ticketId);

        // 3. Verify Analytics Overview
        var overviewRes = await adminClient.GetAsync("/api/v1/analytics/overview?days=30");
        overviewRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var overview = await overviewRes.Content.ReadFromJsonAsync<TenantAnalyticsOverviewDto>();
        overview.Should().NotBeNull();
        overview!.TotalTickets.Should().BeGreaterThan(0);
        overview.SlaComplianceRate.Should().BeInRange(0.0, 100.0);

        // 3b. Verify Ticket Trends (non-UTC host regression test)
        var trendsRes = await adminClient.GetAsync("/api/v1/analytics/tickets/trends?days=14");
        trendsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var trends = await trendsRes.Content.ReadFromJsonAsync<IReadOnlyList<DailyTicketTrendDto>>();
        trends.Should().NotBeNull();
        trends!.Should().HaveCount(14);

        // 4. Verify CSV Export
        var csvRes = await adminClient.GetAsync("/api/v1/analytics/export/tickets.csv?days=30");
        csvRes.StatusCode.Should().Be(HttpStatusCode.OK);
        csvRes.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        csvRes.Content.Headers.ContentDisposition!.FileName.Should().Be("\"nexusdesk-tickets-report.csv\"");
        var csv = await csvRes.Content.ReadAsStringAsync();
        csv.Should().Contain("TicketNumber,Title,Status");

        // 5. Verify HotChocolate GraphQL endpoint /graphql
        var gqlQuery = new
        {
            query = """
                query {
                    tickets(first: 5) {
                        id
                        ticketNumber
                        title
                        status
                    }
                    billingOverview {
                        planCode
                        status
                        maxMonthlyTickets
                    }
                    analyticsOverview(days: 30) {
                        totalTickets
                        slaComplianceRate
                    }
                }
            """
        };

        var gqlRequest = new HttpRequestMessage(HttpMethod.Post, "/graphql")
        {
            Content = new StringContent(JsonSerializer.Serialize(gqlQuery), Encoding.UTF8, "application/json")
        };
        gqlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        gqlRequest.Headers.Add("X-Tenant-Subdomain", "acme");

        var gqlResponse = await adminClient.SendAsync(gqlRequest);
        gqlResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var gqlContent = await gqlResponse.Content.ReadAsStringAsync();
        using var gqlDoc = JsonDocument.Parse(gqlContent);
        gqlDoc.RootElement.TryGetProperty("data", out var data).Should().BeTrue();
        data.TryGetProperty("tickets", out var ticketsGql).Should().BeTrue();
        ticketsGql.EnumerateArray().Should().NotBeEmpty();
        data.TryGetProperty("billingOverview", out var billingGql).Should().BeTrue();
        billingGql.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
        data.TryGetProperty("analyticsOverview", out var analyticsGql).Should().BeTrue();
        analyticsGql.GetProperty("totalTickets").GetInt32().Should().BeGreaterThan(0);

        // 6. Verify Health endpoints
        var healthLive = await adminClient.GetAsync("/health/live");
        healthLive.StatusCode.Should().Be(HttpStatusCode.OK);

        var healthReady = await adminClient.GetAsync("/health/ready");
        healthReady.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
