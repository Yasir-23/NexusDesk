using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Automation.Application;
using NexusDesk.Modules.Automation.Application.Contracts;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.Modules.Ticketing.RealTime;
using NexusDesk.SharedKernel.MultiTenancy;
using Xunit;

namespace NexusDesk.IntegrationTests.Automation;

[Collection("DatabaseIntegrationCollection")]
public class SlaAndAutomationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public SlaAndAutomationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
            builder.UseSetting("SignalR:UseRedisBackplane", "false");
            builder.UseSetting("SlaEvaluator:PollingIntervalSeconds", "60");
        });
    }

    private async Task EnsureTenantsActiveAsync()
    {
        var rootClient = _factory.CreateClient();
        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Acme Corporation", "acme", "admin@acme.com", "Acme Admin", "Pro"));
        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Beta Innovations", "beta", "admin@beta.com", "Beta Admin", "Free"));

        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);
        var listRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        if (listRes.IsSuccessStatusCode)
        {
            var doc = await listRes.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var item in doc.GetProperty("items").EnumerateArray())
            {
                var tid = Guid.Parse(item.GetProperty("id").GetString()!);
                var status = item.GetProperty("status").GetString();
                if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{tid}/status",
                        new UpdateTenantStatusRequest("Active", "Ensure active for SLA & automation integration tests"));
                }
            }
        }
    }

    private async Task<(HttpClient Client, Guid UserId, string Token)> LoginAsAsync(string subdomain, string email, string password = "Password123!")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Login for '{email}' in '{subdomain}' must succeed.");

        var doc = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = doc.GetProperty("accessToken").GetString()!;
        var userId = Guid.Parse(doc.GetProperty("user").GetProperty("id").GetString()!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId, token);
    }

    private async Task<(HttpClient Client, Guid UserId)> RegisterAndLoginCustomerAsync(string subdomain)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var email = $"customer-{Guid.NewGuid():N}@test.local";
        var regRes = await client.PostAsJsonAsync("/api/v1/users/customers/register",
            new RegisterCustomerRequest(email, "SLA Test Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var (authClient, userId, _) = await LoginAsAsync(subdomain, email);
        return (authClient, userId);
    }

    private HubConnection CreateHubConnection(string accessToken)
    {
        var hubUrl = new Uri(_factory.Server.BaseAddress, $"/hubs/tickets?access_token={Uri.EscapeDataString(accessToken)}");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .Build();
    }

    private static async Task<T> WaitForConditionAsync<T>(Func<T?> probe, TimeSpan timeout)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = probe();
            if (result != null)
            {
                return result;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition was not met within {timeout.TotalSeconds}s.");
    }

    [Fact]
    public async Task SlaBreach_ShouldTriggerSignalRAlert_ExecuteAutomationRules_AndEnforceTenantIsolation()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, _, acmeToken) = await LoginAsAsync("acme", "admin@acme.com");
        var (betaAdminClient, _, betaToken) = await LoginAsAsync("beta", "admin@beta.com");
        var (acmeCustomerClient, _) = await RegisterAndLoginCustomerAsync("acme");

        // 1. Verify seeded SLA policies exist for Acme
        var getPoliciesRes = await acmeAdminClient.GetAsync("/api/v1/tenant-admin/sla-policies");
        getPoliciesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialPolicies = await getPoliciesRes.Content.ReadFromJsonAsync<List<SlaPolicyResponse>>(JsonOptions);
        initialPolicies.Should().NotBeNull();
        initialPolicies!.Should().Contain(p => p.Priority == "Low" && p.FirstResponseTargetMinutes == 1440 && p.ResolutionTargetMinutes == 4320);

        // 2. Reject invalid SLA policy input with 400 ProblemDetails
        var invalidPolicyRes = await acmeAdminClient.PutAsJsonAsync("/api/v1/tenant-admin/sla-policies",
            new UpsertSlaPolicyRequest("High", 0, 120));
        invalidPolicyRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Configure custom SLA policy for Acme High priority (5m first response, 30m resolution)
        var upsertRes = await acmeAdminClient.PutAsJsonAsync("/api/v1/tenant-admin/sla-policies",
            new UpsertSlaPolicyRequest("High", 5, 30));
        upsertRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var customPolicy = await upsertRes.Content.ReadFromJsonAsync<SlaPolicyResponse>(JsonOptions);
        customPolicy!.FirstResponseTargetMinutes.Should().Be(5);
        customPolicy.ResolutionTargetMinutes.Should().Be(30);

        try
        {
            // 4. Create automation rules for Acme: escalate High -> Urgent & add internal note on SLA breach
            var escalateRuleRes = await acmeAdminClient.PostAsJsonAsync("/api/v1/tenant-admin/automation-rules",
                new CreateAutomationRuleRequest(
                    "Escalate Breached High Ticket",
                    "TicketSlaBreached",
                    """{"priority":"High"}""",
                    "EscalatePriority",
                    """{"priority":"Urgent"}""",
                    true));
            escalateRuleRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var escalateRule = (await escalateRuleRes.Content.ReadFromJsonAsync<AutomationRuleResponse>(JsonOptions))!;

            var noteRuleRes = await acmeAdminClient.PostAsJsonAsync("/api/v1/tenant-admin/automation-rules",
                new CreateAutomationRuleRequest(
                    "Add SLA Breach Audit Note",
                    "TicketSlaBreached",
                    """{"breachType":"FirstResponse"}""",
                    "AddInternalNote",
                    """{"body":"Automated SLA breach escalation note."}""",
                    true));
            noteRuleRes.StatusCode.Should().Be(HttpStatusCode.Created);

            // Toggle rule test
            var toggleRes = await acmeAdminClient.PatchAsJsonAsync(
                $"/api/v1/tenant-admin/automation-rules/{escalateRule.Id}/toggle",
                new ToggleAutomationRuleRequest(false));
            toggleRes.StatusCode.Should().Be(HttpStatusCode.OK);
            (await toggleRes.Content.ReadFromJsonAsync<AutomationRuleResponse>(JsonOptions))!.IsEnabled.Should().BeFalse();

            var reEnableRes = await acmeAdminClient.PatchAsJsonAsync(
                $"/api/v1/tenant-admin/automation-rules/{escalateRule.Id}/toggle",
                new ToggleAutomationRuleRequest(true));
            reEnableRes.StatusCode.Should().Be(HttpStatusCode.OK);
            (await reEnableRes.Content.ReadFromJsonAsync<AutomationRuleResponse>(JsonOptions))!.IsEnabled.Should().BeTrue();

            // 5. Connect SignalR clients for Acme and Beta
            var acmeBreachAlerts = new ConcurrentQueue<SlaBreachAlertNotification>();
            var betaBreachAlerts = new ConcurrentQueue<SlaBreachAlertNotification>();

            await using var acmeHub = CreateHubConnection(acmeToken);
            await using var betaHub = CreateHubConnection(betaToken);

            acmeHub.On<SlaBreachAlertNotification>(nameof(ITicketHubClient.SlaBreachAlert), a => acmeBreachAlerts.Enqueue(a));
            betaHub.On<SlaBreachAlertNotification>(nameof(ITicketHubClient.SlaBreachAlert), a => betaBreachAlerts.Enqueue(a));

            await acmeHub.StartAsync();
            await betaHub.StartAsync();

            // 6. Customer creates a High priority ticket in Acme -> uses custom 5m / 30m SLA policy
            var createRes = await acmeCustomerClient.PostAsJsonAsync("/api/v1/tickets",
                new CreateTicketRequest("Custom SLA Breach Test", "Should use 5m SLA target", TicketPriority.High, TicketChannel.WebPortal));
            createRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var ticket = (await createRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions))!;

            ticket.FirstResponseDueUtc.Should().BeCloseTo(ticket.CreatedAtUtc.AddMinutes(5), TimeSpan.FromSeconds(2));
            ticket.ResolutionDueUtc.Should().BeCloseTo(ticket.CreatedAtUtc.AddMinutes(30), TimeSpan.FromSeconds(2));
            ticket.IsSlaBreached.Should().BeFalse();

            // 7. Backdate FirstResponseDueUtc in DB so the ticket is breached
            using (var scope = _factory.Services.CreateScope())
            {
                var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                accessor.TenantContext = new TenantContext
                {
                    TenantId = ticket.TenantId,
                    Subdomain = "acme",
                    Status = TenantStatus.Active
                };

                var ticketingDb = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
                var pastDue = DateTimeOffset.UtcNow.AddMinutes(-10);
                await ticketingDb.Tickets
                    .Where(t => t.Id == ticket.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.FirstResponseDueUtc, pastDue));
            }

            // 8. Run SLA breach evaluator
            var evaluatorService = _factory.Services.GetRequiredService<SlaBreachEvaluatorBackgroundService>();
            var breachedCount = await evaluatorService.EvaluateAllTenantsOnceAsync();
            breachedCount.Should().BeGreaterThanOrEqualTo(1);

            // 9. Verify SignalR SlaBreachAlert received by Acme staff and NOT Beta
            var alert = await WaitForConditionAsync(
                () => acmeBreachAlerts.FirstOrDefault(a => a.TicketId == ticket.Id),
                TimeSpan.FromSeconds(5));
            alert.TicketNumber.Should().Be(ticket.TicketNumber);
            alert.Priority.Should().Be("High");
            alert.BreachType.Should().Be("FirstResponse");

            await Task.Delay(200);
            betaBreachAlerts.Should().NotContain(a => a.TicketId == ticket.Id);

            // 10. Verify ticket is marked breached, escalated to Urgent, and has internal note
            var updatedTicketRes = await acmeAdminClient.GetAsync($"/api/v1/tickets/{ticket.Id}");
            updatedTicketRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var updatedTicket = (await updatedTicketRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions))!;

            updatedTicket.IsSlaBreached.Should().BeTrue();
            updatedTicket.SlaBreachedAtUtc.Should().NotBeNull();
            updatedTicket.Priority.Should().Be(TicketPriority.Urgent);
            updatedTicket.Comments.Should().Contain(c =>
                c.Type == CommentType.InternalNote &&
                c.Body == "Automated SLA breach escalation note.");

            // 11. Enforce cross-tenant isolation: Beta cannot read or toggle Acme's rules or see Acme's custom SLA policy
            var betaRulesRes = await betaAdminClient.GetAsync("/api/v1/tenant-admin/automation-rules");
            betaRulesRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var betaRules = await betaRulesRes.Content.ReadFromJsonAsync<List<AutomationRuleResponse>>(JsonOptions);
            betaRules.Should().NotContain(r => r.Id == escalateRule.Id);

            var betaToggleRes = await betaAdminClient.PatchAsJsonAsync(
                $"/api/v1/tenant-admin/automation-rules/{escalateRule.Id}/toggle",
                new ToggleAutomationRuleRequest(false));
            betaToggleRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var betaPoliciesRes = await betaAdminClient.GetAsync("/api/v1/tenant-admin/sla-policies");
            betaPoliciesRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var betaPolicies = await betaPoliciesRes.Content.ReadFromJsonAsync<List<SlaPolicyResponse>>(JsonOptions);
            betaPolicies.Should().Contain(p => p.Priority == "High" && p.FirstResponseTargetMinutes == 120);
        }
        finally
        {
            // Restore Acme High policy back to default (120m / 480m) so other integration test suites pass unmodified
            await acmeAdminClient.PutAsJsonAsync("/api/v1/tenant-admin/sla-policies",
                new UpsertSlaPolicyRequest("High", 120, 480));
        }
    }
}
