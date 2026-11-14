using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Billing.Endpoints;
using NexusDesk.Modules.Billing.Persistence;
using NexusDesk.Modules.Billing.Services;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Billing;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Billing;

public sealed class BillingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Billing";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBillingModule(configuration);
    }
}

public static class BillingModuleExtensions
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBillingSchemaInitializer, BillingSchemaInitializer>();
        services.AddSingleton<IStripeSignatureValidator, StripeSignatureValidator>();
        services.AddScoped<IBillingQuotaService, BillingQuotaService>();

        services.AddDbContext<BillingDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "billing");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(
                sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("billing:read", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("permission", IdentityPermissions.BillingRead) ||
                    ctx.User.IsInRole(IdentityRoles.TenantAdmin) ||
                    ctx.User.IsInRole(IdentityRoles.SuperAdmin));
            });

            options.AddPolicy("billing:manage", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("permission", IdentityPermissions.BillingManage) ||
                    ctx.User.IsInRole(IdentityRoles.TenantAdmin) ||
                    ctx.User.IsInRole(IdentityRoles.SuperAdmin));
            });
        });

        return services;
    }

    public static WebApplication UseBillingModule(this WebApplication app)
    {
        app.MapBillingEndpoints();
        return app;
    }
}
