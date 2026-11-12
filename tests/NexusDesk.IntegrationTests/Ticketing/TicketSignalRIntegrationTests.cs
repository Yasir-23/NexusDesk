using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.RealTime;
using Xunit;

namespace NexusDesk.IntegrationTests.Ticketing;

[Collection("DatabaseIntegrationCollection")]
public class TicketSignalRIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public TicketSignalRIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
            builder.UseSetting("SignalR:UseRedisBackplane", "false");
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
                        new UpdateTenantStatusRequest("Active", "Ensure active for SignalR integration tests"));
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

    private async Task<(HttpClient Client, Guid UserId, string Token)> RegisterAndLoginCustomerAsync(string subdomain)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var email = $"customer-{Guid.NewGuid():N}@test.local";
        var regRes = await client.PostAsJsonAsync("/api/v1/users/customers/register",
            new RegisterCustomerRequest(email, "Test Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        return await LoginAsAsync(subdomain, email);
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
    public async Task StaffShouldReceiveRealtimeTicketEvents_AndCrossTenantShouldBeIsolated()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, _, acmeToken) = await LoginAsAsync("acme", "admin@acme.com");
        var (_, _, betaToken) = await LoginAsAsync("beta", "admin@beta.com");
        var (acmeCustomerClient, _, _) = await RegisterAndLoginCustomerAsync("acme");

        var acmeCreatedEvents = new ConcurrentQueue<TicketRealtimeNotification>();
        var acmeUpdatedEvents = new ConcurrentQueue<TicketRealtimeNotification>();
        var betaCreatedEvents = new ConcurrentQueue<TicketRealtimeNotification>();
        var betaUpdatedEvents = new ConcurrentQueue<TicketRealtimeNotification>();

        await using var acmeConnection = CreateHubConnection(acmeToken);
        await using var betaConnection = CreateHubConnection(betaToken);

        acmeConnection.On<TicketRealtimeNotification>(nameof(ITicketHubClient.TicketCreated), e => acmeCreatedEvents.Enqueue(e));
        acmeConnection.On<TicketRealtimeNotification>(nameof(ITicketHubClient.TicketUpdated), e => acmeUpdatedEvents.Enqueue(e));
        betaConnection.On<TicketRealtimeNotification>(nameof(ITicketHubClient.TicketCreated), e => betaCreatedEvents.Enqueue(e));
        betaConnection.On<TicketRealtimeNotification>(nameof(ITicketHubClient.TicketUpdated), e => betaUpdatedEvents.Enqueue(e));

        await acmeConnection.StartAsync();
        await betaConnection.StartAsync();

        // 1. Create ticket in Acme -> Acme staff receives TicketCreated; Beta does not
        var createRes = await acmeCustomerClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("SignalR live ticket", "Testing real-time creation", TicketPriority.High, TicketChannel.WebPortal));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdTicket = (await createRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions))!;

        var createdNotification = await WaitForConditionAsync(
            () => acmeCreatedEvents.FirstOrDefault(e => e.TicketId == createdTicket.Id),
            TimeSpan.FromSeconds(5));

        createdNotification.TicketNumber.Should().Be(createdTicket.TicketNumber);
        createdNotification.Status.Should().Be(TicketStatus.New);
        createdNotification.EventType.Should().Be("TicketCreated");

        // 2. Update status in Acme -> Acme staff receives TicketUpdated
        var statusRes = await acmeAdminClient.PatchAsJsonAsync($"/api/v1/tickets/{createdTicket.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Open));
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusNotification = await WaitForConditionAsync(
            () => acmeUpdatedEvents.FirstOrDefault(e => e.TicketId == createdTicket.Id && e.EventType == "TicketStatusChanged"),
            TimeSpan.FromSeconds(5));

        statusNotification.Status.Should().Be(TicketStatus.Open);

        // 3. Update priority in Acme -> Acme staff receives TicketUpdated with TicketPriorityChanged
        var priorityRes = await acmeAdminClient.PatchAsJsonAsync($"/api/v1/tickets/{createdTicket.Id}/priority",
            new UpdateTicketPriorityRequest(TicketPriority.Urgent));
        priorityRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var priorityNotification = await WaitForConditionAsync(
            () => acmeUpdatedEvents.FirstOrDefault(e => e.TicketId == createdTicket.Id && e.EventType == "TicketPriorityChanged"),
            TimeSpan.FromSeconds(5));

        priorityNotification.Priority.Should().Be(TicketPriority.Urgent);

        // 4. Verify Beta never received Acme events and cannot join Acme ticket room
        await Task.Delay(250);
        betaCreatedEvents.Should().NotContain(e => e.TicketId == createdTicket.Id);
        betaUpdatedEvents.Should().NotContain(e => e.TicketId == createdTicket.Id);

        var joinAct = async () => await betaConnection.InvokeAsync("JoinTicketRoom", createdTicket.Id);
        await joinAct.Should().ThrowAsync<HubException>().WithMessage("*Ticket not found*");
    }

    [Fact]
    public async Task TicketComments_ShouldFilterInternalNotesFromCustomer_AndDeliverPublicReplies()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, _, acmeStaffToken) = await LoginAsAsync("acme", "admin@acme.com");
        var (customerClient, _, customerToken) = await RegisterAndLoginCustomerAsync("acme");

        var createRes = await customerClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("Comment visibility test", "Testing internal vs public comment filtering", TicketPriority.Medium, TicketChannel.WebPortal));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = (await createRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions))!;

        var staffComments = new ConcurrentQueue<TicketCommentRealtimeNotification>();
        var customerComments = new ConcurrentQueue<TicketCommentRealtimeNotification>();

        await using var staffConnection = CreateHubConnection(acmeStaffToken);
        await using var customerConnection = CreateHubConnection(customerToken);

        staffConnection.On<TicketCommentRealtimeNotification>(nameof(ITicketHubClient.TicketCommentAdded), c => staffComments.Enqueue(c));
        customerConnection.On<TicketCommentRealtimeNotification>(nameof(ITicketHubClient.TicketCommentAdded), c => customerComments.Enqueue(c));

        await staffConnection.StartAsync();
        await customerConnection.StartAsync();

        await staffConnection.InvokeAsync("JoinTicketRoom", ticket.Id);
        await customerConnection.InvokeAsync("JoinTicketRoom", ticket.Id);

        // 1. Staff posts InternalNote -> Staff receives it, Customer MUST NOT
        var internalRes = await acmeAdminClient.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments",
            new AddCommentRequest("Secret internal triage note", CommentType.InternalNote));
        internalRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var staffInternalNote = await WaitForConditionAsync(
            () => staffComments.FirstOrDefault(c => c.TicketId == ticket.Id && c.Type == CommentType.InternalNote),
            TimeSpan.FromSeconds(5));
        staffInternalNote.Body.Should().Be("Secret internal triage note");

        await Task.Delay(250);
        customerComments.Should().NotContain(c => c.Type == CommentType.InternalNote);

        // 2. Staff posts PublicReply -> Customer DOES receive it
        var publicRes = await acmeAdminClient.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments",
            new AddCommentRequest("Hello customer, we are looking into this.", CommentType.PublicReply));
        publicRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var customerPublicReply = await WaitForConditionAsync(
            () => customerComments.FirstOrDefault(c => c.TicketId == ticket.Id && c.Type == CommentType.PublicReply),
            TimeSpan.FromSeconds(5));
        customerPublicReply.Body.Should().Be("Hello customer, we are looking into this.");
    }

    [Fact]
    public async Task AgentCollisionDetection_ShouldBroadcastViewingTypingAndDisconnect_AndExposePresenceEndpoint()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, acmeAdminId, acmeAdminToken) = await LoginAsAsync("acme", "admin@acme.com");
        var (customerClient, _, _) = await RegisterAndLoginCustomerAsync("acme");

        // Create a second staff user if quota allows, otherwise use a second connection as AcmeAdmin
        var agentEmail = $"agent-{Guid.NewGuid():N}@acme.com";
        var secondStaffId = acmeAdminId;
        var secondStaffToken = acmeAdminToken;

        var createAgentRes = await acmeAdminClient.PostAsJsonAsync("/api/v1/users/agents",
            new CreateAgentRequest(agentEmail, "Agent Two", "Password123!"));
        if (createAgentRes.StatusCode == HttpStatusCode.Created)
        {
            var (_, uid, tok) = await LoginAsAsync("acme", agentEmail);
            secondStaffId = uid;
            secondStaffToken = tok;
        }

        var createRes = await customerClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("Collision presence test", "Testing viewing and typing presence", TicketPriority.Low, TicketChannel.WebPortal));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = (await createRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions))!;

        var agentBPresenceEvents = new ConcurrentQueue<TicketPresenceNotification>();

        var agentAConnection = CreateHubConnection(acmeAdminToken);
        await using var agentBConnection = CreateHubConnection(secondStaffToken);

        agentBConnection.On<TicketPresenceNotification>(nameof(ITicketHubClient.AgentPresenceUpdated), p => agentBPresenceEvents.Enqueue(p));

        await agentAConnection.StartAsync();
        await agentBConnection.StartAsync();

        await agentBConnection.InvokeAsync("JoinTicketRoom", ticket.Id);
        await agentAConnection.InvokeAsync("JoinTicketRoom", ticket.Id);

        var joinedNotification = await WaitForConditionAsync(
            () => agentBPresenceEvents.LastOrDefault(p => p.TicketId == ticket.Id && p.ActiveAgents.Any(a => a.UserId == acmeAdminId)),
            TimeSpan.FromSeconds(5));
        joinedNotification.ActiveAgents.Should().Contain(a => a.UserId == acmeAdminId && !a.IsTyping);

        // Agent A starts typing
        await agentAConnection.InvokeAsync("SetTypingState", ticket.Id, true);

        var typingNotification = await WaitForConditionAsync(
            () => agentBPresenceEvents.LastOrDefault(p => p.TicketId == ticket.Id && p.ActiveAgents.Any(a => a.UserId == acmeAdminId && a.IsTyping)),
            TimeSpan.FromSeconds(5));
        typingNotification.ActiveAgents.Should().Contain(a => a.UserId == acmeAdminId && a.IsTyping);

        // REST diagnostic endpoint reflects live presence
        var presenceRes = await acmeAdminClient.GetAsync($"/api/v1/tickets/{ticket.Id}/presence");
        presenceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var presenceBody = (await presenceRes.Content.ReadFromJsonAsync<TicketPresenceResponse>(JsonOptions))!;
        presenceBody.TicketId.Should().Be(ticket.Id);
        presenceBody.ActiveAgents.Should().Contain(a => a.UserId == acmeAdminId && a.IsTyping);
        presenceBody.ActiveAgents.Should().Contain(a => a.UserId == secondStaffId);

        // Agent A disconnects -> if distinct user, Agent A is removed from active agents
        await agentAConnection.DisposeAsync();
        if (secondStaffId != acmeAdminId)
        {
            var disconnectNotification = await WaitForConditionAsync(
                () => agentBPresenceEvents.LastOrDefault(p => p.TicketId == ticket.Id && p.ActiveAgents.All(a => a.UserId != acmeAdminId)),
                TimeSpan.FromSeconds(5));
            disconnectNotification.ActiveAgents.Should().ContainSingle(a => a.UserId == secondStaffId);
        }
    }
}
