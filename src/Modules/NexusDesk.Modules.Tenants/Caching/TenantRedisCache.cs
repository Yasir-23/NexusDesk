using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using StackExchange.Redis;

namespace NexusDesk.Modules.Tenants.Caching;

public record TenantDescriptor(
    Guid TenantId,
    string Subdomain,
    TenantPlanTier PlanTier,
    IsolationStrategy IsolationStrategy,
    TenantStatus Status,
    string? CustomConnectionString,
    int MaxAgents,
    int MaxTicketsPerMonth,
    long MaxStorageBytes);

public interface ITenantCache
{
    Task<TenantDescriptor?> GetOrSetAsync(string slugOrId, CancellationToken cancellationToken = default);
    Task InvalidateAsync(Guid tenantId, string subdomain, CancellationToken cancellationToken = default);
}

public sealed class TenantRedisCache : ITenantCache
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDbContextFactory<CatalogDbContext> _catalogDbContextFactory;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public TenantRedisCache(
        IDbContextFactory<CatalogDbContext> catalogDbContextFactory,
        IConnectionMultiplexer? redis = null)
    {
        _catalogDbContextFactory = catalogDbContextFactory;
        _redis = redis;
    }

    public async Task<TenantDescriptor?> GetOrSetAsync(string slugOrId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slugOrId))
            return null;

        var key = $"tenant:resolver:{slugOrId.Trim().ToLowerInvariant()}";

        // 1. Try Redis cache if available
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cachedJson = await db.StringGetAsync(key);
                if (cachedJson.HasValue)
                {
                    return JsonSerializer.Deserialize<TenantDescriptor>((string)cachedJson!);
                }
            }
            catch
            {
                // Fallback to database on Redis failure
            }
        }

        // 2. Fetch from CatalogDbContext
        await using var catalogDb = await _catalogDbContextFactory.CreateDbContextAsync(cancellationToken);

        Domain.Tenant? tenant;
        if (Guid.TryParse(slugOrId, out var tenantId))
        {
            tenant = await catalogDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        }
        else
        {
            var cleanSubdomain = slugOrId.Trim().ToLowerInvariant();
            tenant = await catalogDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Subdomain == cleanSubdomain, cancellationToken);
        }

        if (tenant == null)
        {
            return null;
        }

        var descriptor = new TenantDescriptor(
            TenantId: tenant.Id,
            Subdomain: tenant.Subdomain,
            PlanTier: tenant.PlanTier,
            IsolationStrategy: tenant.IsolationStrategy,
            Status: tenant.Status,
            CustomConnectionString: tenant.CustomConnectionString,
            MaxAgents: tenant.MaxAgents,
            MaxTicketsPerMonth: tenant.MaxTicketsPerMonth,
            MaxStorageBytes: tenant.MaxStorageBytes);

        // 3. Store in Redis
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var db = _redis.GetDatabase();
                var serialized = JsonSerializer.Serialize(descriptor);
                await db.StringSetAsync(key, serialized, DefaultTtl);
                // Also cache by both id and subdomain for fast lookup
                await db.StringSetAsync($"tenant:resolver:{descriptor.TenantId.ToString().ToLowerInvariant()}", serialized, DefaultTtl);
                await db.StringSetAsync($"tenant:resolver:{descriptor.Subdomain.ToLowerInvariant()}", serialized, DefaultTtl);
            }
            catch
            {
                // Ignored
            }
        }

        return descriptor;
    }

    public async Task InvalidateAsync(Guid tenantId, string subdomain, CancellationToken cancellationToken = default)
    {
        if (_redis == null || !_redis.IsConnected)
            return;

        try
        {
            var db = _redis.GetDatabase();
            var idKey = $"tenant:resolver:{tenantId.ToString().ToLowerInvariant()}";
            var slugKey = $"tenant:resolver:{subdomain.Trim().ToLowerInvariant()}";
            await db.KeyDeleteAsync([idKey, slugKey]);
        }
        catch
        {
            // Ignored
        }
    }
}
