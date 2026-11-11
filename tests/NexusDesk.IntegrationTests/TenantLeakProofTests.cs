using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Xunit;

namespace NexusDesk.IntegrationTests;

[Collection("DatabaseIntegrationCollection")]
public class TenantLeakProofTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    public TenantLeakProofTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        });
    }

    private record TenantTestInfo(Guid Id, string Subdomain, string Name, string Strategy);

    private record SettingItemDto(Guid Id, Guid TenantId, string Key, string Value, DateTimeOffset CreatedAtUtc);

    [Fact]
    public async Task HardChallenge1_100QueryTenantLeakProof_ZeroDataLeakageAcrossTenants()
    {
        var rootClient = _factory.CreateClient();

        // 1. Ensure all 3 tenants (Acme = Pro/Shared, Beta = Free/Shared, Globex = Enterprise/Dedicated) are provisioned
        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Acme Corporation", "acme", "admin@acme.com", "Acme Admin", "Pro"));

        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Beta Innovations", "beta", "admin@beta.com", "Beta Admin", "Free"));

        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Globex Global", "globex", "admin@globex.com", "Globex Admin", "Enterprise"));

        // Ensure all provisioned tenants are Active
        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);
        var adminListRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        var adminDoc = await adminListRes.Content.ReadFromJsonAsync<JsonElement>();
        var itemsProp = adminDoc.TryGetProperty("items", out var ip) ? ip : adminDoc.GetProperty("Items");
        foreach (var item in itemsProp.EnumerateArray())
        {
            var idVal = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : item.GetProperty("Id").GetString();
            var tid = Guid.Parse(idVal!);
            var patchRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{tid}/status", new UpdateTenantStatusRequest("Active", "Ensure active for leak test"));
            patchRes.EnsureSuccessStatusCode();
        }

        // 2. Fetch current tenant descriptors to get their canonical IDs
        var tenants = new List<TenantTestInfo>();
        foreach (var slug in new[] { "acme", "beta", "globex" })
        {
            var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Add("X-Tenant-Subdomain", slug);
            var res = await c.GetAsync("/api/v1/tenant/current");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
            var tid = Guid.Parse(doc.GetProperty("tenantContext").GetProperty("tenantId").GetString()!);
            var strat = doc.GetProperty("tenantContext").GetProperty("isolationStrategy").GetString()!;
            tenants.Add(new TenantTestInfo(tid, slug, slug.ToUpperInvariant(), strat));
        }

        // 3. Seed 10 distinct private records per tenant
        foreach (var t in tenants)
        {
            var tClient = AuthTestTokenHelper.CreateTenantAdminClient(_factory, t.Id, t.Subdomain);

            for (var i = 0; i < 10; i++)
            {
                var key = $"conf.{t.Subdomain}.item_{i}";
                var val = $"secret-payload-for-{t.Subdomain}-{i}-{Guid.NewGuid():N}";
                await tClient.PostAsJsonAsync("/api/v1/tenant/settings/items", new CreateSettingItemRequest(key, val));
            }
        }

        // 4. Execute 100 randomized concurrent and sequential queries across tenants
        const int TotalQueries = 100;
        var leakedRecords = new ConcurrentBag<string>();
        var totalExecutedQueries = 0;

        var random = new Random(42);
        var queryTasks = new List<Func<Task>>();

        for (var i = 0; i < TotalQueries; i++)
        {
            var queryIndex = i;
            var targetTenant = tenants[random.Next(tenants.Count)];
            var headerStrategy = random.Next(4); // 0 = X-Tenant-Subdomain, 1 = X-Tenant-Id (GUID), 2 = X-Tenant-Id (slug), 3 = Host header

            queryTasks.Add(async () =>
            {
                using var client = _factory.CreateClient();

                switch (headerStrategy)
                {
                    case 0:
                        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", targetTenant.Subdomain);
                        break;
                    case 1:
                        client.DefaultRequestHeaders.Add("X-Tenant-Id", targetTenant.Id.ToString());
                        break;
                    case 2:
                        client.DefaultRequestHeaders.Add("X-Tenant-Id", targetTenant.Subdomain);
                        break;
                    case 3:
                        client.DefaultRequestHeaders.Host = $"{targetTenant.Subdomain}.nexusdesk.local";
                        break;
                }

                // Sub-case A: HTTP settings items list
                if (queryIndex % 2 == 0)
                {
                    var response = await client.GetAsync("/api/v1/tenant/settings/items");
                    response.StatusCode.Should().Be(HttpStatusCode.OK, $"Query {queryIndex} for {targetTenant.Subdomain} must succeed.");

                    var items = await response.Content.ReadFromJsonAsync<List<SettingItemDto>>();
                    items.Should().NotBeNull();
                    items!.Count.Should().BeGreaterThan(0, $"Tenant {targetTenant.Subdomain} should return its own seeded settings.");

                    foreach (var item in items!)
                    {
                        if (item.TenantId != targetTenant.Id)
                        {
                            leakedRecords.Add($"LEAK DETECTED: Caller {targetTenant.Subdomain} ({targetTenant.Id}) received record '{item.Key}' owned by tenant '{item.TenantId}'.");
                        }
                    }
                }
                // Sub-case B: Direct TenantScopedDbContext query with LINQ filters
                else
                {
                    using var scope = _factory.Services.CreateScope();
                    var cache = scope.ServiceProvider.GetRequiredService<NexusDesk.Modules.Tenants.Caching.ITenantCache>();
                    var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

                    var desc = await cache.GetOrSetAsync(targetTenant.Subdomain);
                    desc.Should().NotBeNull();

                    accessor.TenantContext = new TenantContext
                    {
                        TenantId = desc!.TenantId,
                        Subdomain = desc.Subdomain,
                        PlanTier = desc.PlanTier,
                        IsolationStrategy = desc.IsolationStrategy,
                        Status = desc.Status,
                        CustomConnectionString = desc.CustomConnectionString
                    };

                    var scopedDb = scope.ServiceProvider.GetRequiredService<TenantScopedDbContext>();
                    var dbItems = await scopedDb.Settings.AsNoTracking().ToListAsync();
                    dbItems.Count.Should().BeGreaterThan(0, $"Tenant {targetTenant.Subdomain} must return its own settings.");

                    foreach (var s in dbItems)
                    {
                        if (s.TenantId != targetTenant.Id)
                        {
                            leakedRecords.Add($"LEAK DETECTED (DbContext): Context {targetTenant.Subdomain} ({targetTenant.Id}) read setting '{s.Key}' with TenantId '{s.TenantId}'.");
                        }
                    }
                }

                Interlocked.Increment(ref totalExecutedQueries);
            });
        }

        // Execute in parallel batches to stress concurrency and async-local context isolation
        var batchSize = 10;
        for (var i = 0; i < queryTasks.Count; i += batchSize)
        {
            var batch = queryTasks.Skip(i).Take(batchSize).Select(t => t());
            await Task.WhenAll(batch);
        }

        // 5. Verification: Mathematical certainty of ZERO cross-tenant leakage
        totalExecutedQueries.Should().Be(TotalQueries, "All 100 queries must execute to completion.");
        leakedRecords.Should().BeEmpty("Hard Challenge #1 Mandate: 0 out of 100 queries may leak cross-tenant records.");
    }
}
