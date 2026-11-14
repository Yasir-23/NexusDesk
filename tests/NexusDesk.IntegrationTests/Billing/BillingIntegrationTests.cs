using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexusDesk.Modules.Billing.Contracts;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Tenants.Endpoints;
using Xunit;

namespace NexusDesk.IntegrationTests.Billing;

[Collection("DatabaseIntegrationCollection")]
public class BillingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";
    private const string StripeWebhookSecret = "whsec_test_secret_nexusdesk_dev_2026";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public BillingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
            builder.UseSetting("SignalR:UseRedisBackplane", "false");
            builder.UseSetting("Billing:StripeWebhookSecret", StripeWebhookSecret);
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
                        new UpdateTenantStatusRequest("Active", "Ensure active for billing integration tests"));
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

        var email = $"billing-cust-{Guid.NewGuid():N}@test.local";
        var regRes = await client.PostAsJsonAsync("/api/v1/users/customers/register",
            new RegisterCustomerRequest(email, "Billing Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        return await LoginAsAsync(subdomain, email);
    }

    private static string ComputeStripeSignature(string payload, string secret, string timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var hex = Convert.ToHexStringLower(hash);
        return $"t={timestamp},v1={hex}";
    }

    [Fact]
    public async Task GetPlans_ShouldReturnActiveCatalog_PubliclyAccessible()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/billing/plans");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var plans = await res.Content.ReadFromJsonAsync<List<SubscriptionPlanDto>>(JsonOptions);
        plans.Should().NotBeNull();
        plans!.Count.Should().BeGreaterOrEqualTo(4);
        plans.Select(p => p.Code).Should().Contain(["Free", "Starter", "Pro", "Enterprise"]);
    }

    [Fact]
    public async Task RoleSecurity_CustomerMustReceive403Forbidden_OnTenantBillingEndpoints()
    {
        await EnsureTenantsActiveAsync();
        var (customerClient, _, _) = await RegisterAndLoginCustomerAsync("acme");

        // 1. GET /api/v1/billing/subscription
        var subRes = await customerClient.GetAsync("/api/v1/billing/subscription");
        subRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Customers must not access billing subscription details.");

        // 2. POST /api/v1/billing/subscription/change-plan
        var changeRes = await customerClient.PostAsJsonAsync("/api/v1/billing/subscription/change-plan",
            new ChangePlanRequest("Enterprise", "Monthly"));
        changeRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Customers must not change subscription plans.");

        // 3. POST /api/v1/billing/subscription/cancel
        var cancelRes = await customerClient.PostAsJsonAsync("/api/v1/billing/subscription/cancel",
            new CancelSubscriptionRequest(false));
        cancelRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Customers must not cancel subscriptions.");

        // 4. GET /api/v1/billing/usage
        var usageRes = await customerClient.GetAsync("/api/v1/billing/usage");
        usageRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Customers must not read billing usage.");

        // 5. POST /api/v1/billing/usage/record
        var recordRes = await customerClient.PostAsJsonAsync("/api/v1/billing/usage/record",
            new RecordUsageRequest("tickets_created", 1));
        recordRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Customers must not mutate usage meters.");

        // 6. GET /api/v1/billing/invoices
        var invoiceRes = await customerClient.GetAsync("/api/v1/billing/invoices");
        invoiceRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Customers must not view billing invoices.");
    }

    [Fact]
    public async Task MultiTenantIsolation_AcmeAndBeta_MustNeverLeakBillingData()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdmin, _, _) = await LoginAsAsync("acme", "admin@acme.com");
        var (betaAdmin, _, _) = await LoginAsAsync("beta", "admin@beta.com");

        // 1. Inspect subscriptions
        var acmeSubRes = await acmeAdmin.GetAsync("/api/v1/billing/subscription");
        acmeSubRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var acmeSub = await acmeSubRes.Content.ReadFromJsonAsync<TenantSubscriptionDetailsDto>(JsonOptions);
        acmeSub.Should().NotBeNull();
        acmeSub!.Plan.Code.Should().Be("Pro");

        var betaSubRes = await betaAdmin.GetAsync("/api/v1/billing/subscription");
        betaSubRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaSub = await betaSubRes.Content.ReadFromJsonAsync<TenantSubscriptionDetailsDto>(JsonOptions);
        betaSub.Should().NotBeNull();
        betaSub!.Plan.Code.Should().Be("Free");

        // Subscriptions must belong to distinct tenants
        acmeSub.Subscription.TenantId.Should().NotBe(betaSub.Subscription.TenantId);

        // 2. Record usage in Acme
        var recordRes = await acmeAdmin.PostAsJsonAsync("/api/v1/billing/usage/record",
            new RecordUsageRequest("ai_tokens_used", 5000));
        recordRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify Beta's usage for ai_tokens_used does NOT reflect Acme's usage
        var betaUsageRes = await betaAdmin.GetAsync("/api/v1/billing/usage");
        betaUsageRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaUsage = await betaUsageRes.Content.ReadFromJsonAsync<UsagePeriodDto>(JsonOptions);
        betaUsage.Should().NotBeNull();
        var betaAiTokens = betaUsage!.Metrics.FirstOrDefault(m => m.MetricName == "ai_tokens_used");
        betaAiTokens.Should().NotBeNull();
        betaAiTokens!.Used.Should().Be(0, "Beta must not see Acme's usage consumption.");
    }

    [Fact]
    public async Task EndToEndBillingFlow_QuotaEnforcement_DowngradeConflict_UpgradeAndInvoice_AndStripeWebhook()
    {
        await EnsureTenantsActiveAsync();

        var uniqueSubdomain = $"flow-{Guid.NewGuid():N}"[..16];
        var uniqueEmail = $"admin@{uniqueSubdomain}.local";
        var rootClient = _factory.CreateClient();
        var provRes = await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Flow Tenant", uniqueSubdomain, uniqueEmail, "Flow Admin", "Free"));
        provRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);
        var listRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        if (listRes.IsSuccessStatusCode)
        {
            var doc = await listRes.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var item in doc.GetProperty("items").EnumerateArray())
            {
                var tid = Guid.Parse(item.GetProperty("id").GetString()!);
                var sub = item.GetProperty("subdomain").GetString();
                if (string.Equals(sub, uniqueSubdomain, StringComparison.OrdinalIgnoreCase))
                {
                    await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{tid}/status",
                        new UpdateTenantStatusRequest("Active", "Activate for flow test"));
                }
            }
        }

        var (tenantAdmin, _, _) = await LoginAsAsync(uniqueSubdomain, uniqueEmail);

        // 1. Check tenant subscription (starts on Free plan, max 100 tickets)
        var subRes = await tenantAdmin.GetAsync("/api/v1/billing/subscription");
        subRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var subDetails = await subRes.Content.ReadFromJsonAsync<TenantSubscriptionDetailsDto>(JsonOptions);
        subDetails.Should().NotBeNull();
        subDetails!.Plan.MaxMonthlyTickets.Should().Be(100);

        // 2. Increment usage near limit (95 tickets)
        var rec1 = await tenantAdmin.PostAsJsonAsync("/api/v1/billing/usage/record",
            new RecordUsageRequest("tickets_created", 95));
        rec1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Exceed quota limit (95 + 10 = 105 > 100) -> Must return 429 Plan Quota Exceeded
        var rec2 = await tenantAdmin.PostAsJsonAsync("/api/v1/billing/usage/record",
            new RecordUsageRequest("tickets_created", 10));
        rec2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = await rec2.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Plan Quota Exceeded");
        var errorCode = problem.TryGetProperty("errorCode", out var ec)
            ? ec.GetString()
            : problem.GetProperty("extensions").GetProperty("errorCode").GetString();
        errorCode.Should().Be("PLAN_QUOTA_EXCEEDED");

        // 4. Upgrade tenant to Pro plan ($99 / 9900 cents)
        var upgradeRes = await tenantAdmin.PostAsJsonAsync("/api/v1/billing/subscription/change-plan",
            new ChangePlanRequest("Pro", "Monthly"));
        upgradeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedSub = await upgradeRes.Content.ReadFromJsonAsync<TenantSubscriptionDto>(JsonOptions);
        updatedSub!.PlanCode.Should().Be("Pro");

        // Verify generated BillingInvoice for paid upgrade
        var invRes = await tenantAdmin.GetAsync("/api/v1/billing/invoices");
        invRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var invoices = await invRes.Content.ReadFromJsonAsync<List<BillingInvoiceDto>>(JsonOptions);
        invoices.Should().NotBeNull();
        invoices.Should().Contain(i => i.PlanCode == "Pro" && i.AmountCents == 9900 && i.Status == "Paid");

        // 5. Test Downgrade Conflict:
        // Current tickets_created is 95. Target plan Free limit is 100.
        // Let's add 10 more tickets on Pro (now total 105).
        var rec3 = await tenantAdmin.PostAsJsonAsync("/api/v1/billing/usage/record",
            new RecordUsageRequest("tickets_created", 10));
        rec3.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempting to downgrade to Free (max tickets 100 < current 105) must fail with 409 Conflict
        var downgradeRes = await tenantAdmin.PostAsJsonAsync("/api/v1/billing/subscription/change-plan",
            new ChangePlanRequest("Free", "Monthly"));
        downgradeRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var downgradeConflict = await downgradeRes.Content.ReadFromJsonAsync<JsonElement>();
        downgradeConflict.GetProperty("title").GetString().Should().Be("Plan Downgrade Conflict");

        // 6. Stripe Webhook Testing
        var anonymousClient = _factory.CreateClient();
        var eventId = $"evt_test_{Guid.NewGuid():N}";
        var stripePayload = $$"""
        {
          "id": "{{eventId}}",
          "type": "invoice.paid",
          "data": {
            "object": {
              "id": "in_test_{{Guid.NewGuid():N}}",
              "customer": "cus_test_{{uniqueSubdomain}}",
              "subscription": "sub_test_{{uniqueSubdomain}}",
              "amount_paid": 9900,
              "currency": "usd",
              "status": "paid",
              "hosted_invoice_url": "https://stripe.com/test_invoice",
              "metadata": {
                "tenant_id": "{{subDetails.Subscription.TenantId}}"
              }
            }
          }
        }
        """;

        // 6a. Send with missing signature -> 401 Unauthorized
        var noSigReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhooks/stripe")
        {
            Content = new StringContent(stripePayload, Encoding.UTF8, "application/json")
        };
        var noSigRes = await anonymousClient.SendAsync(noSigReq);
        noSigRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 6b. Send with valid HMAC-SHA256 signature -> 200 OK
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var validHeader = ComputeStripeSignature(stripePayload, StripeWebhookSecret, ts);

        var validReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhooks/stripe")
        {
            Content = new StringContent(stripePayload, Encoding.UTF8, "application/json")
        };
        validReq.Headers.Add("Stripe-Signature", validHeader);

        var validRes = await anonymousClient.SendAsync(validReq);
        validRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var validDoc = await validRes.Content.ReadFromJsonAsync<JsonElement>();
        validDoc.GetProperty("received").GetBoolean().Should().BeTrue();
        validDoc.GetProperty("eventId").GetString().Should().Be(eventId);

        // 6c. Replay duplicate event -> 200 OK with duplicate: true (Idempotency)
        var replayReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/webhooks/stripe")
        {
            Content = new StringContent(stripePayload, Encoding.UTF8, "application/json")
        };
        replayReq.Headers.Add("Stripe-Signature", validHeader);

        var replayRes = await anonymousClient.SendAsync(replayReq);
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayDoc = await replayRes.Content.ReadFromJsonAsync<JsonElement>();
        replayDoc.GetProperty("received").GetBoolean().Should().BeTrue();
        replayDoc.GetProperty("duplicate").GetBoolean().Should().BeTrue();
    }
}
