using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexusDesk.Modules.Tenants.Endpoints;
using Npgsql;
using Xunit;

namespace NexusDesk.IntegrationTests;

[Collection("DatabaseIntegrationCollection")]
public class TenantProvisioningAndHybridRoutingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    public TenantProvisioningAndHybridRoutingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        });
    }

    [Fact]
    public async Task HybridMultiTenancy_Provisioning_Routing_And_SuspensionGate_ShouldSucceed()
    {
        var client = _factory.CreateClient();

        // 1. Provision Acme (Pro -> SharedDatabase)
        var acmeReq = new ProvisionTenantRequest("Acme Corporation", "acme", "admin@acme.com", "Acme Admin", "Pro");
        var acmeRes = await client.PostAsJsonAsync("/api/v1/tenants/provision", acmeReq);
        var acmeError = await acmeRes.Content.ReadAsStringAsync();
        acmeRes.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, acmeError);

        // 2. Provision Beta (Free -> SharedDatabase)
        var betaReq = new ProvisionTenantRequest("Beta Innovations", "beta", "admin@beta.com", "Beta Admin", "Free");
        var betaRes = await client.PostAsJsonAsync("/api/v1/tenants/provision", betaReq);
        betaRes.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);

        // 3. Provision Globex (Enterprise -> DedicatedDatabase)
        var globexReq = new ProvisionTenantRequest("Globex Global", "globex", "admin@globex.com", "Globex Admin", "Enterprise");
        var globexRes = await client.PostAsJsonAsync("/api/v1/tenants/provision", globexReq);
        globexRes.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);

        // 4. Verify physical dedicated database exists in PostgreSQL pg_database
        await using (var connection = new NpgsqlConnection(PostgresConnectionString))
        {
            await connection.OpenAsync();
            var checkSql = "SELECT 1 FROM pg_database WHERE datname = 'nexusdesk_tenant_globex';";
            await using var cmd = new NpgsqlCommand(checkSql, connection);
            var exists = await cmd.ExecuteScalarAsync();
            exists.Should().NotBeNull("Physical dedicated database 'nexusdesk_tenant_globex' must exist in PostgreSQL.");
        }

        // 5. Test routing for Acme (SharedDatabase)
        var acmeClient = _factory.CreateClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");
        var acmeCurrentRes = await acmeClient.GetAsync("/api/v1/tenant/current");
        acmeCurrentRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var acmeJson = await acmeCurrentRes.Content.ReadFromJsonAsync<JsonElement>();
        acmeJson.GetProperty("activeDatabase").GetString().Should().Be("nexusdesk_shared");
        acmeJson.GetProperty("tenantContext").GetProperty("isolationStrategy").GetString().Should().Be("SharedDatabase");
        acmeJson.GetProperty("tenantContext").GetProperty("planTier").GetString().Should().Be("Pro");
        var acmeTenantId = acmeJson.GetProperty("tenantContext").GetProperty("tenantId").GetString();
        acmeJson.GetProperty("rlsSessionTenantId").GetString().Should().Be(acmeTenantId);

        // 6. Test routing for Globex (DedicatedDatabase)
        var globexClient = _factory.CreateClient();
        globexClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "globex");
        var globexCurrentRes = await globexClient.GetAsync("/api/v1/tenant/current");
        globexCurrentRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var globexJson = await globexCurrentRes.Content.ReadFromJsonAsync<JsonElement>();
        globexJson.GetProperty("activeDatabase").GetString().Should().Be("nexusdesk_tenant_globex");
        globexJson.GetProperty("tenantContext").GetProperty("isolationStrategy").GetString().Should().Be("DedicatedDatabase");
        globexJson.GetProperty("tenantContext").GetProperty("planTier").GetString().Should().Be("Enterprise");

        // 7. Test Suspended Tenant Gate on Beta
        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);
        var adminListRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        adminListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminListDoc = await adminListRes.Content.ReadFromJsonAsync<JsonElement>();
        var betaItem = adminListDoc.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("subdomain").GetString() == "beta");
        var betaTenantId = Guid.Parse(betaItem.GetProperty("id").GetString()!);

        // Ensure Beta is Active before testing suspension
        var resetRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status", new UpdateTenantStatusRequest("Active", "Reset state"));
        resetRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var betaClient = _factory.CreateClient();
        betaClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

        try
        {
            var betaCurrentPre = await betaClient.GetAsync("/api/v1/tenant/current");
            betaCurrentPre.StatusCode.Should().Be(HttpStatusCode.OK);
            var suspendRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status", new UpdateTenantStatusRequest("Suspended", "Non-payment"));
            suspendRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // Calling operational route on suspended tenant must return 403 Forbidden with TENANT_SUSPENDED
            var betaSuspendedRes = await betaClient.GetAsync("/api/v1/tenant/current");
            betaSuspendedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var suspendedProblem = await betaSuspendedRes.Content.ReadFromJsonAsync<JsonElement>();
            string? errorCode = null;
            if (suspendedProblem.TryGetProperty("errorCode", out var ec))
            {
                errorCode = ec.GetString();
            }
            else if (suspendedProblem.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("errorCode", out var ec2))
            {
                errorCode = ec2.GetString();
            }
            errorCode.Should().Be("TENANT_SUSPENDED");
        }
        finally
        {
            // Reactivate Beta unconditionally
            var reactivateRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status", new UpdateTenantStatusRequest("Active", "Payment received"));
            reactivateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Calling operational route on reactivated tenant must succeed
        var betaReactivatedRes = await betaClient.GetAsync("/api/v1/tenant/current");
        betaReactivatedRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
