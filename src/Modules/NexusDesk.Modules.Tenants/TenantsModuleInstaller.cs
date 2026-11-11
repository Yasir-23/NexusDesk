using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Tenants.Caching;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;
using StackExchange.Redis;

namespace NexusDesk.Modules.Tenants;

public sealed class TenantsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Tenants";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTenantsModule(configuration);
    }
}

public static class TenantsModuleExtensions
{
    public static IServiceCollection AddTenantsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Core multi-tenant context accessor
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();

        // 2. Data Protection for encrypting connection strings
        services.AddDataProtection();

        // 3. Interceptors
        services.AddScoped<TenantAuditSaveChangesInterceptor>();
        services.AddScoped<PostgresRlsConnectionInterceptor>();

        // 4. Hybrid Connection Resolver & Provisioner
        services.AddSingleton<ITenantConnectionResolver, TenantConnectionResolver>();
        services.AddScoped<ITenantDatabaseProvisioner, TenantDatabaseProvisioner>();
        services.AddScoped<ITenantSchemaInitializer, TenantSchemaInitializer>();

        // 5. CatalogDbContext (Global master plane in nexusdesk_shared)
        services.AddDbContextFactory<CatalogDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            options.UseNpgsql(resolver.GetDefaultConnectionString());
        });

        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
            return factory.CreateDbContext();
        });

        // 6. TenantScopedDbContext (Routes to shared or dedicated DB per tenant)
        services.AddDbContext<TenantScopedDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr)
                   .AddInterceptors(
                       sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                       sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        // 7. Tenant Cache (Redis + fallback to CatalogDb)
        services.AddSingleton<ITenantCache, TenantRedisCache>();

        return services;
    }
}
