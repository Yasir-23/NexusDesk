using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Endpoints;
using Xunit;

namespace NexusDesk.IntegrationTests.Portal;

[Collection("DatabaseIntegrationCollection")]
public class CustomerPortalIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public CustomerPortalIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
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
                        new UpdateTenantStatusRequest("Active", "Ensure active for portal integration tests"));
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

        var email = $"cust-{Guid.NewGuid():N}@portal-test.local";
        var regRes = await client.PostAsJsonAsync("/api/v1/users/customers/register",
            new RegisterCustomerRequest(email, "Portal Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var (authClient, userId, _) = await LoginAsAsync(subdomain, email);
        return (authClient, userId);
    }

    [Fact]
    public async Task CustomerPortal_LifecycleAndSecurityBoundaries_ShouldIsolateAndStripInternalNotes()
    {
        await EnsureTenantsActiveAsync();

        // 1. Register Customer A and Customer B in 'acme', and Customer C in 'beta'
        var (custAClient, custAId) = await RegisterAndLoginCustomerAsync("acme");
        var (custBClient, custBId) = await RegisterAndLoginCustomerAsync("acme");
        var (custCClient, custCId) = await RegisterAndLoginCustomerAsync("beta");

        // 2. Customer A creates a new ticket via portal
        var createTicketReq = new PortalCreateTicketRequest(
            "Portal Ticket 1",
            "Customer description for portal ticket",
            TicketPriority.High,
            ["self-service", "portal"]);

        var createRes = await custAClient.PostAsJsonAsync("/api/v1/portal/tickets", createTicketReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdTicket = await createRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        createdTicket.Should().NotBeNull();
        createdTicket!.CustomerId.Should().Be(custAId);
        createdTicket.Channel.Should().Be(TicketChannel.WebPortal);
        createdTicket.Priority.Should().Be(TicketPriority.High);
        createdTicket.ActivityLogs.Should().BeEmpty(); // Activity logs must be blanked for customers

        var ticketId = createdTicket.Id;

        // 3. Customer A views their own ticket list via GET /api/v1/portal/tickets
        var listRes = await custAClient.GetAsync("/api/v1/portal/tickets");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedTickets = await listRes.Content.ReadFromJsonAsync<PagedTicketsResponse>(JsonOptions);
        pagedTickets.Should().NotBeNull();
        pagedTickets!.Items.Should().Contain(t => t.Id == ticketId);

        // 4. Agent in 'acme' logs in and adds both an internal note and a public reply
        var (agentClient, agentId, _) = await LoginAsAsync("acme", "admin@acme.com");

        var internalNoteRes = await agentClient.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/comments",
            new AddCommentRequest("AGENT INTERNAL NOTE: Customer might be confused, check server logs.", CommentType.InternalNote));
        internalNoteRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var publicReplyRes = await agentClient.PostAsJsonAsync($"/api/v1/tickets/{ticketId}/comments",
            new AddCommentRequest("AGENT PUBLIC REPLY: We are investigating your inquiry.", CommentType.PublicReply));
        publicReplyRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 5. Customer A fetches ticket details via GET /api/v1/portal/tickets/{id}
        // CRITICAL SECURITY REQUIREMENT: InternalNote comments and ActivityLogs MUST NEVER be leaked
        var getDetailsRes = await custAClient.GetAsync($"/api/v1/portal/tickets/{ticketId}");
        getDetailsRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var details = await getDetailsRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        details.Should().NotBeNull();
        details!.Id.Should().Be(ticketId);

        // Comments must only contain PublicReply
        details.Comments.Should().HaveCount(1);
        details.Comments.Should().OnlyContain(c => c.Type == CommentType.PublicReply);
        details.Comments[0].Body.Should().Be("AGENT PUBLIC REPLY: We are investigating your inquiry.");
        details.Comments.Should().NotContain(c => c.Type == CommentType.InternalNote);
        details.Comments.Should().NotContain(c => c.Body.Contains("check server logs"));

        // ActivityLogs must be strictly empty
        details.ActivityLogs.Should().BeEmpty();

        // 6. Customer A posts a reply via POST /api/v1/portal/tickets/{id}/replies
        var custReplyRes = await custAClient.PostAsJsonAsync($"/api/v1/portal/tickets/{ticketId}/replies",
            new PortalTicketReplyRequest("CUSTOMER REPLY: Thank you for looking into it."));
        custReplyRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var custReplyDto = await custReplyRes.Content.ReadFromJsonAsync<TicketCommentDto>(JsonOptions);
        custReplyDto.Should().NotBeNull();
        custReplyDto!.IsCustomer.Should().BeTrue();
        custReplyDto.Type.Should().Be(CommentType.PublicReply);
        custReplyDto.Body.Should().Be("CUSTOMER REPLY: Thank you for looking into it.");

        // 7. Customer B in same tenant ('acme') tries to view Customer A's ticket -> MUST RETURN 404
        var custBAccessRes = await custBClient.GetAsync($"/api/v1/portal/tickets/{ticketId}");
        custBAccessRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B tries to reply to Customer A's ticket -> MUST RETURN 404
        var custBReplyRes = await custBClient.PostAsJsonAsync($"/api/v1/portal/tickets/{ticketId}/replies",
            new PortalTicketReplyRequest("Customer B malicious reply"));
        custBReplyRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer B's ticket list should NOT contain Customer A's ticket
        var custBListRes = await custBClient.GetAsync("/api/v1/portal/tickets");
        custBListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var custBPaged = await custBListRes.Content.ReadFromJsonAsync<PagedTicketsResponse>(JsonOptions);
        custBPaged!.Items.Should().NotContain(t => t.Id == ticketId);

        // 8. Cross-tenant Customer C in 'beta' tries to view Customer A's ticket -> MUST RETURN 404
        var custCAccessRes = await custCClient.GetAsync($"/api/v1/portal/tickets/{ticketId}");
        custCAccessRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Customer C tries to reply -> MUST RETURN 404
        var custCReplyRes = await custCClient.PostAsJsonAsync($"/api/v1/portal/tickets/{ticketId}/replies",
            new PortalTicketReplyRequest("Customer C cross-tenant reply"));
        custCReplyRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 9. Agent calling /api/v1/portal/tickets gets 403 Forbidden
        var agentPortalAccessRes = await agentClient.GetAsync("/api/v1/portal/tickets");
        agentPortalAccessRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
