using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Analytics.Endpoints;
using NexusDesk.Modules.Analytics.Persistence;
using NexusDesk.Modules.Analytics.Services;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Audit;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Notifications;

namespace NexusDesk.Modules.Analytics;

public sealed class AnalyticsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Analytics";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAnalyticsModule(configuration);
    }
}

public static class AnalyticsModuleExtensions
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAnalyticsSchemaInitializer, AnalyticsSchemaInitializer>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddDbContext<AnalyticsDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "analytics");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(
                sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("analytics:read", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("permission", IdentityPermissions.AnalyticsRead) ||
                    ctx.User.IsInRole(IdentityRoles.Agent) ||
                    ctx.User.IsInRole(IdentityRoles.TenantAdmin) ||
                    ctx.User.IsInRole(IdentityRoles.SuperAdmin));
            });

            options.AddPolicy("audit:read", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("permission", IdentityPermissions.AuditRead) ||
                    ctx.User.IsInRole(IdentityRoles.TenantAdmin) ||
                    ctx.User.IsInRole(IdentityRoles.SuperAdmin));
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder UseAnalyticsModule(this IEndpointRouteBuilder app)
    {
        app.MapAnalyticsEndpoints();
        app.MapAuditLogEndpoints();
        app.MapNotificationEndpoints();
        return app;
    }
}
