using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Automation.Application;
using NexusDesk.Modules.Automation.Application.Contracts;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Xunit;

namespace NexusDesk.IntegrationTests.Automation;

[Collection("DatabaseIntegrationCollection")]
public class WebhookReliabilityAndDlqIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public WebhookReliabilityAndDlqIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
            builder.UseSetting("SignalR:UseRedisBackplane", "false");
            builder.UseSetting("Outbox:PollingIntervalSeconds", "3600");
            builder.UseSetting("Outbox:UseFastTestBackoff", "true");
        });
    }

    [AllowCrossTenantQuery("Test setup cleans up webhook subscriptions, outbox messages, and dead letters")]
    private async Task ResetWebhookDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
        await db.DeadLetterWebhooks.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.OutboxMessages.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.WebhookSubscriptions.IgnoreQueryFilters().ExecuteDeleteAsync();
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
                        new UpdateTenantStatusRequest("Active", "Ensure active for webhook integration tests"));
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

    [Fact]
    [AllowCrossTenantQuery("Test setup cleans up webhook data")]
    public async Task HappyPath_TicketCreated_ShouldDeliverSignedWebhook_WithValidHmacHeaders()
    {
        await EnsureTenantsActiveAsync();
        await ResetWebhookDataAsync();
        await using var receiver = new TestWebhookReceiver();

        var (adminClient, _, _) = await LoginAsAsync("acme", "admin@acme.com");

        const string webhookSecret = "acme-happy-path-secret-key-32-chars";
        var createSubRes = await adminClient.PostAsJsonAsync("/api/v1/tenant-admin/webhooks",
            new CreateWebhookSubscriptionRequest(
                receiver.Url,
                webhookSecret,
                ["ticket.created"],
                IsActive: true));

        createSubRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var subDto = await createSubRes.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>();
        subDto.Should().NotBeNull();
        subDto!.TargetUrl.Should().Be(receiver.Url);

        // Create a ticket in Acme tenant
        var createTicketRes = await adminClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest(
                "Webhook Happy Path Test Ticket",
                "Verifying HMAC signature delivery",
                TicketPriority.High,
                TicketChannel.WebPortal));

        createTicketRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await createTicketRes.Content.ReadFromJsonAsync<TicketDetailsResponse>();
        ticket.Should().NotBeNull();

        // Dispatch outbox messages
        var dispatcher = _factory.Services.GetRequiredService<OutboxWebhookDispatcherBackgroundService>();
        await dispatcher.ProcessPendingOutboxMessagesOnceAsync();

        // Assert receiver got the delivery
        receiver.ReceivedRequests.Should().NotBeEmpty();
        receiver.ReceivedRequests.TryDequeue(out var received).Should().BeTrue();
        received.Should().NotBeNull();

        received!.HttpMethod.Should().Be("POST");
        received.Headers.Should().ContainKey(WebhookSigner.HeaderEvent);
        received.Headers[WebhookSigner.HeaderEvent].Should().Be("ticket.created");
        received.Headers.Should().ContainKey(WebhookSigner.HeaderDelivery);
        received.Headers.Should().ContainKey(WebhookSigner.HeaderTimestamp);
        received.Headers.Should().ContainKey(WebhookSigner.HeaderSignature256);

        var timestamp = received.Headers[WebhookSigner.HeaderTimestamp];
        var signature = received.Headers[WebhookSigner.HeaderSignature256];

        // Verify cryptographic HMAC-SHA256 signature
        var isValidSignature = WebhookSigner.VerifySignature(
            webhookSecret,
            timestamp,
            received.Body,
            signature);

        isValidSignature.Should().BeTrue("Webhook receiver must verify HMAC-SHA256 signature matches");

        // Verify JSON payload envelope
        using var jsonDoc = JsonDocument.Parse(received.Body);
        var root = jsonDoc.RootElement;
        root.GetProperty("eventType").GetString().Should().Be("ticket.created");
        root.GetProperty("data").GetProperty("ticketId").GetString().Should().Be(ticket!.Id.ToString());
        root.GetProperty("data").GetProperty("ticketNumber").GetString().Should().Be(ticket.TicketNumber);

        // Verify outbox entry is marked Processed
        var outboxRes = await adminClient.GetAsync("/api/v1/tenant-admin/webhooks/outbox");
        outboxRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var outboxMessages = await outboxRes.Content.ReadFromJsonAsync<List<OutboxMessageResponse>>();
        outboxMessages.Should().NotBeNull();
        var delivered = outboxMessages!.FirstOrDefault(m => m.PayloadJson.Contains(ticket.Id.ToString()));
        delivered.Should().NotBeNull();
        delivered!.Status.Should().Be("Processed");
        delivered.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    [AllowCrossTenantQuery("Integration test modifies outbox retry timestamps directly to verify Challenge 6 backoff and DLQ")]
    public async Task Challenge6_FaultInjection_ShouldRetry3TimesWithBackoff_AndMoveToDeadLetterQueue()
    {
        await EnsureTenantsActiveAsync();
        await ResetWebhookDataAsync();
        await using var receiver = new TestWebhookReceiver();

        // Simulate 503 Service Unavailable across all delivery attempts
        receiver.ResponseGenerator = _ => (503, """{"error":"Service Unavailable"}""");

        var (adminClient, _, _) = await LoginAsAsync("acme", "admin@acme.com");

        const string webhookSecret = "acme-fault-injection-secret-32-chars";
        var createSubRes = await adminClient.PostAsJsonAsync("/api/v1/tenant-admin/webhooks",
            new CreateWebhookSubscriptionRequest(
                receiver.Url,
                webhookSecret,
                ["ticket.created"],
                IsActive: true));

        createSubRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var subDto = await createSubRes.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>();
        subDto.Should().NotBeNull();

        // Create a ticket to generate an outbox event
        var createTicketRes = await adminClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest(
                "Challenge 6 DLQ Test Ticket",
                "Simulating 503 receiver fault injection",
                TicketPriority.Urgent,
                TicketChannel.Email));

        createTicketRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await createTicketRes.Content.ReadFromJsonAsync<TicketDetailsResponse>();
        ticket.Should().NotBeNull();

        var dispatcher = _factory.Services.GetRequiredService<OutboxWebhookDispatcherBackgroundService>();

        Guid outboxMessageId;

        // Attempt 1
        await dispatcher.ProcessPendingOutboxMessagesOnceAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
            var allMessages = await db.OutboxMessages.IgnoreQueryFilters()
                .Where(m => m.EventType == "ticket.created")
                .ToListAsync();
            var msg = allMessages.First(m => m.PayloadJson.Contains(ticket!.Id.ToString()));
            outboxMessageId = msg.Id;

            receiver.Errors.Should().BeEmpty();
            receiver.ReceivedRequests.Should().NotBeEmpty(
                $"Receiver must receive delivery request (URL: {receiver.Url}), but received {receiver.ReceivedRequests.Count} requests and errors: {string.Join("; ", receiver.Errors.Select(e => e.Message))}");
            msg.RetryCount.Should().Be(1);
            msg.Status.Should().Be(OutboxMessageStatus.Pending);
            msg.LastError.Should().Contain("503");

            // Advance NextRetryAtUtc to simulate backoff elapsing for attempt 2
            msg.ScheduleNextRetryAt(DateTimeOffset.UtcNow.AddSeconds(-1));
            await db.SaveChangesAsync();
        }

        // Attempt 2
        await dispatcher.ProcessPendingOutboxMessagesOnceAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
            var msg = await db.OutboxMessages.IgnoreQueryFilters()
                .FirstAsync(m => m.Id == outboxMessageId);

            msg.RetryCount.Should().Be(2);
            msg.Status.Should().Be(OutboxMessageStatus.Pending);

            // Advance NextRetryAtUtc to simulate backoff elapsing for attempt 3
            msg.ScheduleNextRetryAt(DateTimeOffset.UtcNow.AddSeconds(-1));
            await db.SaveChangesAsync();
        }

        // Attempt 3 -> Should transition to DeadLettered
        await dispatcher.ProcessPendingOutboxMessagesOnceAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutomationDbContext>();
            var msg = await db.OutboxMessages.IgnoreQueryFilters()
                .FirstAsync(m => m.Id == outboxMessageId);

            msg.RetryCount.Should().Be(3);
            msg.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        }

        // Verify Dead-Letter endpoint returns the failed delivery
        var dlqRes = await adminClient.GetAsync("/api/v1/tenant-admin/webhooks/dead-letters");
        dlqRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var deadLetters = await dlqRes.Content.ReadFromJsonAsync<List<DeadLetterWebhookResponse>>();
        deadLetters.Should().NotBeNull();

        var dlqRecord = deadLetters!.FirstOrDefault(d => d.PayloadJson.Contains(ticket!.Id.ToString()));
        dlqRecord.Should().NotBeNull("Failed delivery must land in DeadLetterWebhooks");
        dlqRecord!.AttemptCount.Should().Be(3);
        dlqRecord.LastHttpStatusCode.Should().Be(503);
        dlqRecord.EventType.Should().Be("ticket.created");
        dlqRecord.TargetUrl.Should().Be(receiver.Url);
        dlqRecord.LastError.Should().Contain("503");
    }

    [Fact]
    [AllowCrossTenantQuery("Test setup cleans up webhook data")]
    public async Task CrossTenantIsolation_BetaTenant_CannotAccessOrMutateAcmeWebhooksOrDeadLetters()
    {
        await EnsureTenantsActiveAsync();
        await ResetWebhookDataAsync();

        var (acmeAdmin, _, _) = await LoginAsAsync("acme", "admin@acme.com");
        var (betaAdmin, _, _) = await LoginAsAsync("beta", "admin@beta.com");

        // Acme registers a webhook subscription
        var createSubRes = await acmeAdmin.PostAsJsonAsync("/api/v1/tenant-admin/webhooks",
            new CreateWebhookSubscriptionRequest(
                "https://acme-isolated.example.com/webhook",
                "acme-secret-isolation-test-32-chars",
                ["ticket.created"]));

        createSubRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var acmeSub = await createSubRes.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>();
        acmeSub.Should().NotBeNull();

        // 1. Beta lists webhooks -> Must not see Acme's subscription
        var betaListRes = await betaAdmin.GetAsync("/api/v1/tenant-admin/webhooks");
        betaListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaSubs = await betaListRes.Content.ReadFromJsonAsync<List<WebhookSubscriptionResponse>>();
        betaSubs.Should().NotContain(s => s.Id == acmeSub!.Id);

        // 2. Beta attempts to delete Acme's subscription -> Must return 404 Not Found
        var betaDeleteRes = await betaAdmin.DeleteAsync($"/api/v1/tenant-admin/webhooks/{acmeSub!.Id}");
        betaDeleteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 3. Beta attempts to toggle Acme's subscription -> Must return 404 Not Found
        var betaToggleRes = await betaAdmin.PatchAsJsonAsync($"/api/v1/tenant-admin/webhooks/{acmeSub.Id}/toggle", new { });
        betaToggleRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Beta reads outbox messages -> Must not see any Acme outbox messages
        var betaOutboxRes = await betaAdmin.GetAsync("/api/v1/tenant-admin/webhooks/outbox");
        betaOutboxRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaOutbox = await betaOutboxRes.Content.ReadFromJsonAsync<List<OutboxMessageResponse>>();
        betaOutbox.Should().NotContain(m => m.TenantId == acmeSub.TenantId);

        // 5. Beta reads dead letters -> Must not see any Acme dead letters
        var betaDlqRes = await betaAdmin.GetAsync("/api/v1/tenant-admin/webhooks/dead-letters");
        betaDlqRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaDlq = await betaDlqRes.Content.ReadFromJsonAsync<List<DeadLetterWebhookResponse>>();
        betaDlq.Should().NotContain(d => d.TenantId == acmeSub.TenantId);
    }
}

public sealed class TestWebhookReceiver : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listenTask;

    public ConcurrentQueue<ReceivedWebhookRequest> ReceivedRequests { get; } = new();
    public ConcurrentQueue<Exception> Errors { get; } = new();
    public Func<HttpListenerRequest, (int StatusCode, string Body)> ResponseGenerator { get; set; } = _ => (200, "{\"status\":\"ok\"}");

    public string Url { get; }

    public TestWebhookReceiver()
    {
        var port = FindFreePort();
        Url = $"http://localhost:{port}/webhook/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _listenTask = Task.Run(ListenLoopAsync);
    }

    private static int FindFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                string body = string.Empty;
                if (context.Request.ContentLength64 > 0)
                {
                    var buffer = new byte[context.Request.ContentLength64];
                    var totalRead = 0;
                    while (totalRead < buffer.Length)
                    {
                        var bytesRead = await context.Request.InputStream.ReadAsync(buffer, totalRead, buffer.Length - totalRead);
                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }
                    body = Encoding.UTF8.GetString(buffer, 0, totalRead);
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string? key in context.Request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        headers[key] = context.Request.Headers[key]!;
                    }
                }

                ReceivedRequests.Enqueue(new ReceivedWebhookRequest(
                    context.Request.HttpMethod,
                    context.Request.RawUrl ?? string.Empty,
                    headers,
                    body));

                var (statusCode, responseBody) = ResponseGenerator(context.Request);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(responseBody);
                context.Response.Close(bytes, false);
            }
            catch when (_cts.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (Exception ex)
            {
                Errors.Enqueue(ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Ignored on dispose
        }

        await Task.WhenAny(_listenTask, Task.Delay(500));
        _cts.Dispose();
    }
}

public sealed record ReceivedWebhookRequest(
    string HttpMethod,
    string RawUrl,
    IReadOnlyDictionary<string, string> Headers,
    string Body);
