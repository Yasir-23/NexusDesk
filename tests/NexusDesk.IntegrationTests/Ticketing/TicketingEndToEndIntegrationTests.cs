using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Caching;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Xunit;

namespace NexusDesk.IntegrationTests.Ticketing;

[Collection("DatabaseIntegrationCollection")]
public class TicketingEndToEndIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public TicketingEndToEndIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        });
    }

    private async Task EnsureTenantsActiveAsync()
    {
        await using (var conn = new Npgsql.NpgsqlConnection("Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable"))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("DELETE FROM billing.usage_meter_records WHERE metric_name = 'tickets_created';", conn);
            await cmd.ExecuteNonQueryAsync();
        }

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
                        new UpdateTenantStatusRequest("Active", "Ensure active for ticketing integration tests"));
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
            new RegisterCustomerRequest(email, "Test Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var (authClient, userId, _) = await LoginAsAsync(subdomain, email);
        return (authClient, userId);
    }

    [Fact]
    public async Task EndToEndLifecycle_CreateAssignCommentPendingResolveCloseReopen_ShouldPersistAndEnforceRules()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, acmeAdminId, _) = await LoginAsAsync("acme", "admin@acme.com");
        var (customerClient, customerId) = await RegisterAndLoginCustomerAsync("acme");

        // Try creating an agent in Acme (or fall back to Acme TenantAdmin if Pro 15-agent quota was filled by prior runs)
        var agentEmail = $"agent-{Guid.NewGuid():N}@acme.com";
        Guid agentId = acmeAdminId;
        HttpClient agentClient = acmeAdminClient;

        var createAgentRes = await acmeAdminClient.PostAsJsonAsync("/api/v1/users/agents",
            new CreateAgentRequest(agentEmail, "Acme Support Agent", "Password123!"));
        if (createAgentRes.StatusCode == HttpStatusCode.Created)
        {
            var agentJson = await createAgentRes.Content.ReadFromJsonAsync<JsonElement>();
            agentId = Guid.Parse(agentJson.GetProperty("id").GetString()!);
            var (loggedAgentClient, _, _) = await LoginAsAsync("acme", agentEmail);
            agentClient = loggedAgentClient;
        }

        // 1. Customer creates Ticket #1 (High priority)
        var createReq1 = new CreateTicketRequest(
            "Payment webhook timeout",
            "Our production endpoint is timing out on retries.",
            TicketPriority.High,
            TicketChannel.WebPortal,
            null,
            ["Billing", "Webhook"]);

        var createRes1 = await customerClient.PostAsJsonAsync("/api/v1/tickets", createReq1, JsonOptions);
        createRes1.StatusCode.Should().Be(HttpStatusCode.Created);

        var ticket1 = await createRes1.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        ticket1.Should().NotBeNull();
        ticket1!.TicketNumber.Should().StartWith("TICK-");
        ticket1.Status.Should().Be(TicketStatus.New);
        ticket1.Priority.Should().Be(TicketPriority.High);
        ticket1.CustomerId.Should().Be(customerId);
        ticket1.Tags.Should().BeEquivalentTo(["billing", "webhook"]);
        ticket1.FirstResponseDueUtc.Should().BeCloseTo(ticket1.CreatedAtUtc.AddHours(2), TimeSpan.FromSeconds(2));
        ticket1.ResolutionDueUtc.Should().BeCloseTo(ticket1.CreatedAtUtc.AddHours(8), TimeSpan.FromSeconds(2));

        var firstSeq = long.Parse(ticket1.TicketNumber.Replace("TICK-", string.Empty));

        // 2. Agent creates Ticket #2 (Urgent priority) and verifies sequential numbering
        var createRes2 = await agentClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("SSO down", "All users locked out.", TicketPriority.Urgent, TicketChannel.Api, customerId),
            JsonOptions);
        createRes2.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket2 = await createRes2.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        var secondSeq = long.Parse(ticket2!.TicketNumber.Replace("TICK-", string.Empty));
        secondSeq.Should().BeGreaterThan(firstSeq);

        // 3. Paginated list filtered by status & priority
        var pagedRes = await agentClient.GetAsync("/api/v1/tickets?status=New&priority=High&page=1&pageSize=10");
        pagedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await pagedRes.Content.ReadFromJsonAsync<PagedTicketsResponse>(JsonOptions);
        paged!.Items.Should().Contain(t => t.Id == ticket1.Id);
        paged.Items.Should().NotContain(t => t.Id == ticket2.Id);

        // 4. Assign ticket1 to agent -> auto-transitions New -> Open
        var assignRes = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/assign",
            new AssignTicketRequest(agentId),
            JsonOptions);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var assignedTicket = await assignRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        assignedTicket!.AssignedAgentId.Should().Be(agentId);
        assignedTicket.Status.Should().Be(TicketStatus.Open);

        // 5. Agent adds InternalNote -> customer must NOT see it, FirstRespondedAtUtc remains null
        var internalNoteRes = await agentClient.PostAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/comments",
            new AddCommentRequest("Internal triage: checking Redis worker.", CommentType.InternalNote),
            JsonOptions);
        internalNoteRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var customerViewAfterNote = await customerClient.GetFromJsonAsync<TicketDetailsResponse>($"/api/v1/tickets/{ticket1.Id}", JsonOptions);
        customerViewAfterNote!.Comments.Should().BeEmpty("Customers must not see internal notes.");
        customerViewAfterNote.FirstRespondedAtUtc.Should().BeNull();

        // Customer attempting to post InternalNote -> 403 Forbidden
        var forbiddenNoteRes = await customerClient.PostAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/comments",
            new AddCommentRequest("Sneaky internal note", CommentType.InternalNote),
            JsonOptions);
        forbiddenNoteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 6. Agent adds PublicReply -> sets FirstRespondedAtUtc and customer sees it
        var publicReplyRes = await agentClient.PostAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/comments",
            new AddCommentRequest("We reproduced the issue and are deploying a fix.", CommentType.PublicReply),
            JsonOptions);
        publicReplyRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var customerViewAfterReply = await customerClient.GetFromJsonAsync<TicketDetailsResponse>($"/api/v1/tickets/{ticket1.Id}", JsonOptions);
        customerViewAfterReply!.Comments.Should().ContainSingle(c => c.Type == CommentType.PublicReply);
        customerViewAfterReply.FirstRespondedAtUtc.Should().NotBeNull();

        // 7. Agent sets status to PendingCustomer -> Customer reply automatically transitions back to Open
        var pendingRes = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.PendingCustomer),
            JsonOptions);
        pendingRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pendingRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions))!.Status.Should().Be(TicketStatus.PendingCustomer);

        var customerFollowUpRes = await customerClient.PostAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/comments",
            new AddCommentRequest("Confirmed, please proceed.", CommentType.PublicReply),
            JsonOptions);
        customerFollowUpRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var reopenedByCustomer = await agentClient.GetFromJsonAsync<TicketDetailsResponse>($"/api/v1/tickets/{ticket1.Id}", JsonOptions);
        reopenedByCustomer!.Status.Should().Be(TicketStatus.Open);

        // 8. Resolve requires notes
        var resolveMissingNotes = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Resolved, ResolutionNotes: ""),
            JsonOptions);
        resolveMissingNotes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var resolveOk = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Resolved, ResolutionNotes: "Increased webhook timeout threshold."),
            JsonOptions);
        resolveOk.StatusCode.Should().Be(HttpStatusCode.OK);
        var resolvedTicket = await resolveOk.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        resolvedTicket!.Status.Should().Be(TicketStatus.Resolved);
        resolvedTicket.ResolvedAtUtc.Should().NotBeNull();

        // 9. Close ticket
        var closeOk = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Closed),
            JsonOptions);
        closeOk.StatusCode.Should().Be(HttpStatusCode.OK);
        var closedTicket = await closeOk.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        closedTicket!.Status.Should().Be(TicketStatus.Closed);
        closedTicket.ClosedAtUtc.Should().NotBeNull();

        // Cannot resolve a closed ticket without reopening
        var resolveClosed = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Resolved, ResolutionNotes: "Attempt on closed"),
            JsonOptions);
        resolveClosed.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 10. Reopen closed ticket with reason
        var reopenOk = await agentClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{ticket1.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Open, Reason: "Customer reported regression"),
            JsonOptions);
        reopenOk.StatusCode.Should().Be(HttpStatusCode.OK);
        var reopenedTicket = await reopenOk.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        reopenedTicket!.Status.Should().Be(TicketStatus.Open);
        reopenedTicket.ActivityLogs.Should().Contain(a => a.Action == "Reopened");
    }

    [Fact]
    public async Task TenantIsolation_CrossTenantReadListAssignCommentAndStatus_ShouldBeBlocked()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, _, acmeToken) = await LoginAsAsync("acme", "admin@acme.com");
        var (betaAdminClient, betaAdminId, _) = await LoginAsAsync("beta", "admin@beta.com");

        // Create a ticket in Beta
        var betaTicketRes = await betaAdminClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("Beta Confidential Incident", "Secret tenant beta payload", TicketPriority.Urgent),
            JsonOptions);
        betaTicketRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var betaTicket = await betaTicketRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);

        // Create a ticket in Acme
        var acmeTicketRes = await acmeAdminClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("Acme Ticket", "Acme payload", TicketPriority.Low),
            JsonOptions);
        acmeTicketRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var acmeTicket = await acmeTicketRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);

        // 1. Cross-tenant header mismatch: Acme JWT + X-Tenant-Subdomain: beta -> 403 Forbidden
        var spoofedClient = _factory.CreateClient();
        spoofedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acmeToken);
        spoofedClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

        var spoofedGet = await spoofedClient.GetAsync($"/api/v1/tickets/{betaTicket!.Id}");
        spoofedGet.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Acme user in Acme context querying Beta's ticket ID -> 404 NotFound (global query filter + RLS)
        var crossGet = await acmeAdminClient.GetAsync($"/api/v1/tickets/{betaTicket.Id}");
        crossGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossAssign = await acmeAdminClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{betaTicket.Id}/assign",
            new AssignTicketRequest(betaAdminId),
            JsonOptions);
        crossAssign.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossStatus = await acmeAdminClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{betaTicket.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.Closed),
            JsonOptions);
        crossStatus.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossComment = await acmeAdminClient.PostAsJsonAsync(
            $"/api/v1/tickets/{betaTicket.Id}/comments",
            new AddCommentRequest("Cross-tenant probe", CommentType.PublicReply),
            JsonOptions);
        crossComment.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 3. Acme user cannot assign an Acme ticket to a Beta agent
        var assignForeignAgent = await acmeAdminClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{acmeTicket!.Id}/assign",
            new AssignTicketRequest(betaAdminId),
            JsonOptions);
        assignForeignAgent.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 4. Acme list query never includes Beta tickets
        var acmeList = await acmeAdminClient.GetFromJsonAsync<PagedTicketsResponse>("/api/v1/tickets?page=1&pageSize=100", JsonOptions);
        acmeList!.Items.Should().NotContain(t => t.Id == betaTicket.Id);
    }

    [Fact]
    public async Task OptimisticConcurrency_StaleRowVersionViaHttpAndConcurrentDbContexts_ShouldDetectConflict()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdminClient, acmeAdminId, _) = await LoginAsAsync("acme", "admin@acme.com");

        var createRes = await acmeAdminClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("Concurrency Test Ticket", "Testing xmin row version", TicketPriority.Medium),
            JsonOptions);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        var staleRowVersion = created!.RowVersion;

        // 1. First update succeeds with staleRowVersion
        var update1 = await acmeAdminClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{created.Id}/assign",
            new AssignTicketRequest(acmeAdminId, staleRowVersion),
            JsonOptions);
        update1.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated1 = await update1.Content.ReadFromJsonAsync<TicketDetailsResponse>(JsonOptions);
        updated1!.RowVersion.Should().NotBe(staleRowVersion);

        // 2. Second update using the old staleRowVersion -> 409 Conflict
        var update2 = await acmeAdminClient.PatchAsJsonAsync(
            $"/api/v1/tickets/{created.Id}/status",
            new UpdateTicketStatusRequest(TicketStatus.PendingCustomer, RowVersion: staleRowVersion),
            JsonOptions);
        update2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Direct DbContext concurrency test with two concurrent scopes
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();

        var cache1 = scope1.ServiceProvider.GetRequiredService<ITenantCache>();
        var acmeDesc = await cache1.GetOrSetAsync("acme");
        acmeDesc.Should().NotBeNull();

        var accessor1 = scope1.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        accessor1.TenantContext = new TenantContext
        {
            TenantId = acmeDesc!.TenantId,
            Subdomain = acmeDesc.Subdomain,
            PlanTier = acmeDesc.PlanTier,
            IsolationStrategy = acmeDesc.IsolationStrategy,
            Status = acmeDesc.Status,
            CustomConnectionString = acmeDesc.CustomConnectionString
        };

        var accessor2 = scope2.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        accessor2.TenantContext = accessor1.TenantContext;

        var db1 = scope1.ServiceProvider.GetRequiredService<TicketingDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<TicketingDbContext>();

        var t1 = await db1.Tickets.FirstAsync(t => t.Id == created.Id);
        var t2 = await db2.Tickets.FirstAsync(t => t.Id == created.Id);

        t1.ChangePriority(TicketPriority.Urgent, acmeAdminId).IsSuccess.Should().BeTrue();
        await db1.SaveChangesAsync();

        t2.ChangePriority(TicketPriority.Low, acmeAdminId).IsSuccess.Should().BeTrue();
        var act = async () => await db2.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task SwaggerAndEfMigrations_ShouldExposeTicketingSchemasAndHaveNoPendingModelChanges()
    {
        var client = _factory.CreateClient();

        // 1. Verify OpenAPI schemas & requestBody definitions for all Ticketing endpoints
        var swaggerRes = await client.GetAsync("/swagger/v1/swagger.json");
        swaggerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var swaggerJson = await swaggerRes.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(swaggerJson);
        var paths = doc.RootElement.GetProperty("paths");

        paths.GetProperty("/api/v1/tickets").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _).Should().BeTrue();
        paths.GetProperty("/api/v1/tickets/{id}/assign").GetProperty("patch")
            .GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _).Should().BeTrue();
        paths.GetProperty("/api/v1/tickets/{id}/status").GetProperty("patch")
            .GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _).Should().BeTrue();
        paths.GetProperty("/api/v1/tickets/{id}/comments").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _).Should().BeTrue();
        paths.GetProperty("/api/v1/tickets/{id}/attachments").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _).Should().BeTrue();

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.TryGetProperty("CreateTicketRequest", out _).Should().BeTrue();
        schemas.TryGetProperty("AssignTicketRequest", out _).Should().BeTrue();
        schemas.TryGetProperty("UpdateTicketStatusRequest", out _).Should().BeTrue();
        schemas.TryGetProperty("AddCommentRequest", out _).Should().BeTrue();
        schemas.TryGetProperty("TicketDetailsResponse", out _).Should().BeTrue();
        schemas.TryGetProperty("TicketSummaryResponse", out _).Should().BeTrue();
        schemas.TryGetProperty("PagedTicketsResponse", out _).Should().BeTrue();

        // 2. Verify TicketingDbContext model snapshot has zero pending model changes
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketingDbContext>();
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task TicketAttachments_UploadListAndDownload_ShouldSucceedWithTenantIsolation()
    {
        await EnsureTenantsActiveAsync();

        var (customerClient, _) = await RegisterAndLoginCustomerAsync("acme");

        // 1. Customer creates a ticket
        var createTicketRes = await customerClient.PostAsJsonAsync("/api/v1/tickets",
            new CreateTicketRequest("Attachment Test Ticket", "Testing ticket file attachments", TicketPriority.Medium));
        createTicketRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await createTicketRes.Content.ReadFromJsonAsync<TicketDetailsResponse>();
        ticket.Should().NotBeNull();
        var ticketId = ticket!.Id;

        // 2. Customer uploads a valid PNG attachment
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "screenshot.png");

        var uploadRes = await customerClient.PostAsync($"/api/v1/tickets/{ticketId}/attachments", content);
        uploadRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var attachmentDto = await uploadRes.Content.ReadFromJsonAsync<TicketAttachmentDto>();
        attachmentDto.Should().NotBeNull();
        attachmentDto!.FileName.Should().Be("screenshot.png");
        attachmentDto.SizeBytes.Should().Be(pngBytes.Length);

        // 3. Customer lists attachments
        var listRes = await customerClient.GetAsync($"/api/v1/tickets/{ticketId}/attachments");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var attachments = await listRes.Content.ReadFromJsonAsync<List<TicketAttachmentDto>>();
        attachments.Should().NotBeNull();
        attachments!.Should().ContainSingle(a => a.Id == attachmentDto.Id);

        // 4. Customer downloads attachment
        var downloadRes = await customerClient.GetAsync($"/api/v1/tickets/{ticketId}/attachments/{attachmentDto.Id}/download");
        downloadRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadedBytes = await downloadRes.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().Equal(pngBytes);

        // 5. Cross-tenant isolation: Beta customer cannot access Acme ticket attachment
        var (betaCustomerClient, _) = await RegisterAndLoginCustomerAsync("beta");
        var crossTenantListRes = await betaCustomerClient.GetAsync($"/api/v1/tickets/{ticketId}/attachments");
        crossTenantListRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
