using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using NexusDesk.Modules.Chat.Contracts;
using NexusDesk.Modules.Chat.Domain;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Endpoints;
using Xunit;

namespace NexusDesk.IntegrationTests.Chat;

[Collection("DatabaseIntegrationCollection")]
public class LiveChatLifecycleAndSignalRIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public LiveChatLifecycleAndSignalRIntegrationTests(WebApplicationFactory<Program> factory)
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
                        new UpdateTenantStatusRequest("Active", "Ensure active for chat integration tests"));
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

        var email = $"chat-cust-{Guid.NewGuid():N}@test.local";
        var regRes = await client.PostAsJsonAsync("/api/v1/users/customers/register",
            new RegisterCustomerRequest(email, "Chat Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        return await LoginAsAsync(subdomain, email);
    }

    private HubConnection CreateChatHubConnection(string accessToken)
    {
        var hubUrl = new Uri(_factory.Server.BaseAddress, $"/hubs/chat?access_token={Uri.EscapeDataString(accessToken)}");
        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();
    }

    [Fact]
    public async Task EndToEndLiveChatLifecycle_WithSignalRHub_ShouldClaimMessageAndConvertToTicket()
    {
        await EnsureTenantsActiveAsync();

        // 1. Arrange Customer A, Customer B, and Agent in 'acme'
        var (custAClient, custAId, custAToken) = await RegisterAndLoginCustomerAsync("acme");
        var (custBClient, custBId, _) = await RegisterAndLoginCustomerAsync("acme");
        var (agentClient, agentId, agentToken) = await LoginAsAsync("acme", "admin@acme.com");

        // 2. Customer A starts a new live chat session (Status = Waiting)
        var createSessionReq = new CreateChatSessionRequest("Need urgent billing support");
        var createRes = await custAClient.PostAsJsonAsync("/api/v1/chat/sessions", createSessionReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var sessionDto = await createRes.Content.ReadFromJsonAsync<ChatSessionDto>(JsonOptions);
        sessionDto.Should().NotBeNull();
        sessionDto!.Status.Should().Be(ChatSessionStatus.Waiting);
        sessionDto.CustomerId.Should().Be(custAId);
        sessionDto.AssignedAgentId.Should().BeNull();
        sessionDto.Subject.Should().Be("Need urgent billing support");

        var sessionId = sessionDto.Id;

        // 3. Agent views chat queue via GET /api/v1/chat/sessions
        var agentListRes = await agentClient.GetAsync("/api/v1/chat/sessions?status=Waiting");
        agentListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var queue = await agentListRes.Content.ReadFromJsonAsync<List<ChatSessionSummaryDto>>(JsonOptions);
        queue.Should().NotBeNull();
        queue!.Should().Contain(s => s.Id == sessionId && s.Status == ChatSessionStatus.Waiting);

        // 4. Setup SignalR connections for both Customer A and Agent
        var receivedClaimEvents = new ConcurrentBag<ChatSessionDto>();
        var receivedMessagesCustomer = new ConcurrentBag<ChatMessageDto>();
        var receivedMessagesAgent = new ConcurrentBag<ChatMessageDto>();
        var receivedClosedEvents = new ConcurrentBag<ChatSessionClosedDto>();
        var receivedTypingEvents = new ConcurrentBag<ChatTypingDto>();

        await using var custHubConn = CreateChatHubConnection(custAToken);
        await using var agentHubConn = CreateChatHubConnection(agentToken);

        custHubConn.On<ChatSessionDto>("ChatSessionClaimed", dto => receivedClaimEvents.Add(dto));
        custHubConn.On<ChatMessageDto>("ChatMessageReceived", msg => receivedMessagesCustomer.Add(msg));
        custHubConn.On<ChatSessionClosedDto>("ChatSessionClosed", dto => receivedClosedEvents.Add(dto));
        custHubConn.On<ChatTypingDto>("ChatTypingIndicator", typing => receivedTypingEvents.Add(typing));

        agentHubConn.On<ChatSessionDto>("ChatSessionClaimed", dto => receivedClaimEvents.Add(dto));
        agentHubConn.On<ChatMessageDto>("ChatMessageReceived", msg => receivedMessagesAgent.Add(msg));
        agentHubConn.On<ChatSessionClosedDto>("ChatSessionClosed", dto => receivedClosedEvents.Add(dto));
        agentHubConn.On<ChatTypingDto>("ChatTypingIndicator", typing => receivedTypingEvents.Add(typing));

        await custHubConn.StartAsync();
        await agentHubConn.StartAsync();

        // Both join the session group
        await custHubConn.InvokeAsync("JoinChatSession", sessionId);
        await agentHubConn.InvokeAsync("JoinChatSession", sessionId);

        // 5. Agent claims the session via POST /api/v1/chat/sessions/{id}/claim
        var claimRes = await agentClient.PostAsync($"/api/v1/chat/sessions/{sessionId}/claim", null);
        claimRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var claimedDto = await claimRes.Content.ReadFromJsonAsync<ChatSessionDto>(JsonOptions);
        claimedDto.Should().NotBeNull();
        claimedDto!.Status.Should().Be(ChatSessionStatus.Active);
        claimedDto.AssignedAgentId.Should().Be(agentId);

        // Verify SignalR ChatSessionClaimed event received
        await Task.Delay(200);
        receivedClaimEvents.Should().Contain(e => e.Id == sessionId && e.Status == ChatSessionStatus.Active);

        // 6. Customer sends typing indicator over SignalR
        await custHubConn.InvokeAsync("SendTypingIndicator", sessionId, true);
        await Task.Delay(200);
        receivedTypingEvents.Should().Contain(t => t.SessionId == sessionId && t.UserId == custAId && t.IsTyping);

        // 7. Customer sends message via POST /api/v1/chat/sessions/{id}/messages
        var custMsgRes = await custAClient.PostAsJsonAsync($"/api/v1/chat/sessions/{sessionId}/messages",
            new SendChatMessageRequest(Content: "Hello, my subscription renewal failed today."));
        custMsgRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var custMsgDto = await custMsgRes.Content.ReadFromJsonAsync<ChatMessageDto>(JsonOptions);
        custMsgDto.Should().NotBeNull();
        custMsgDto!.SenderUserId.Should().Be(custAId);
        custMsgDto.SenderRole.Should().Be("Customer");
        custMsgDto.Content.Should().Be("Hello, my subscription renewal failed today.");

        // Verify Agent received message via SignalR
        await Task.Delay(200);
        receivedMessagesAgent.Should().Contain(m => m.Id == custMsgDto.Id && m.Content == custMsgDto.Content);

        // 8. Agent sends reply message
        var agentMsgRes = await agentClient.PostAsJsonAsync($"/api/v1/chat/sessions/{sessionId}/messages",
            new SendChatMessageRequest(Content: "I see your account, let me verify your payment method."));
        agentMsgRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var agentMsgDto = await agentMsgRes.Content.ReadFromJsonAsync<ChatMessageDto>(JsonOptions);
        agentMsgDto.Should().NotBeNull();
        agentMsgDto!.SenderUserId.Should().Be(agentId);
        agentMsgDto.SenderRole.Should().Be("Agent");

        // Verify Customer received message via SignalR
        await Task.Delay(200);
        receivedMessagesCustomer.Should().Contain(m => m.Id == agentMsgDto.Id && m.Content == agentMsgDto.Content);

        // 9. Customer views session details via GET /api/v1/chat/sessions/{id}
        var getSessionRes = await custAClient.GetAsync($"/api/v1/chat/sessions/{sessionId}");
        getSessionRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionWithHistory = await getSessionRes.Content.ReadFromJsonAsync<ChatSessionDto>(JsonOptions);
        sessionWithHistory.Should().NotBeNull();
        sessionWithHistory!.Messages.Should().HaveCount(2);
        sessionWithHistory.Messages![0].Content.Should().Be("Hello, my subscription renewal failed today.");
        sessionWithHistory.Messages[1].Content.Should().Be("I see your account, let me verify your payment method.");

        // 10. Agent closes session with convertToTicket: true
        var closeRes = await agentClient.PostAsJsonAsync($"/api/v1/chat/sessions/{sessionId}/close",
            new CloseChatSessionRequest(ConvertToTicket: true));
        closeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var closedSessionDto = await closeRes.Content.ReadFromJsonAsync<ChatSessionDto>(JsonOptions);
        closedSessionDto.Should().NotBeNull();
        closedSessionDto!.Status.Should().Be(ChatSessionStatus.ConvertedToTicket);
        closedSessionDto.ConvertedTicketId.Should().NotBeNull();
        closedSessionDto.ConvertedTicketId.Should().NotBe(Guid.Empty);
        closedSessionDto.ClosedAtUtc.Should().NotBeNull();

        // Verify SignalR ChatSessionClosed received
        await Task.Delay(200);
        receivedClosedEvents.Should().Contain(e => e.Id == sessionId && e.Status == ChatSessionStatus.ConvertedToTicket);

        // 11. Sending further messages on closed session must return 409 Conflict
        var conflictMsgRes = await custAClient.PostAsJsonAsync($"/api/v1/chat/sessions/{sessionId}/messages",
            new SendChatMessageRequest(Content: "Should return conflict"));
        conflictMsgRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 12. Unauthorized Customer B attempting to view Customer A's session -> MUST RETURN 404
        var custBAccessRes = await custBClient.GetAsync($"/api/v1/chat/sessions/{sessionId}");
        custBAccessRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
