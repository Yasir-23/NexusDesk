using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.Automation;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Automation.Application;

public sealed class SlaBreachEvaluatorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaBreachEvaluatorBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval;

    public SlaBreachEvaluatorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SlaBreachEvaluatorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var seconds = configuration.GetValue<int?>("SlaEvaluator:PollingIntervalSeconds") ?? 15;
        if (seconds < 1)
        {
            seconds = 15;
        }

        _pollingInterval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAllTenantsOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during SLA breach evaluation cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> EvaluateAllTenantsOnceAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantDescriptor> activeTenants;
        using (var catalogScope = _scopeFactory.CreateScope())
        {
            var catalogDb = catalogScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            activeTenants = await GetActiveTenantsAsync(catalogDb, cancellationToken);
        }

        var totalBreached = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var tenant in activeTenants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var tenantScope = _scopeFactory.CreateScope();
                var tenantAccessor = tenantScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenantAccessor.TenantContext = new TenantContext
                {
                    TenantId = tenant.TenantId,
                    Subdomain = tenant.Subdomain,
                    IsolationStrategy = tenant.IsolationStrategy,
                    CustomConnectionString = tenant.CustomConnectionString,
                    Status = TenantStatus.Active
                };

                var evaluator = tenantScope.ServiceProvider.GetRequiredService<ITicketSlaBreachEvaluator>();
                totalBreached += await evaluator.EvaluateTenantBreachesAsync(tenant.TenantId, now, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed evaluating SLA breaches for tenant {TenantId} ({Subdomain}).",
                    tenant.TenantId,
                    tenant.Subdomain);
            }
        }

        return totalBreached;
    }

    [AllowCrossTenantQuery]
    private static async Task<IReadOnlyList<TenantDescriptor>> GetActiveTenantsAsync(
        CatalogDbContext catalogDb,
        CancellationToken cancellationToken)
    {
        return await catalogDb.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => new TenantDescriptor(
                t.Id,
                t.Subdomain,
                t.IsolationStrategy,
                t.CustomConnectionString))
            .ToListAsync(cancellationToken);
    }

    private sealed record TenantDescriptor(
        Guid TenantId,
        string Subdomain,
        IsolationStrategy IsolationStrategy,
        string? CustomConnectionString);
}
