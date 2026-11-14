using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.KnowledgeBase.Endpoints;
using NexusDesk.Modules.KnowledgeBase.Persistence;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.KnowledgeBase;

public sealed class KnowledgeBaseModuleInstaller : IModuleInstaller
{
    public string ModuleName => "KnowledgeBase";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddKnowledgeBaseModule(configuration);
    }
}

public static class KnowledgeBaseModuleExtensions
{
    public static IServiceCollection AddKnowledgeBaseModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IKnowledgeBaseSchemaInitializer, KnowledgeBaseSchemaInitializer>();

        services.AddDbContext<KnowledgeBaseDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "kb");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(
                sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("kb:read", policy =>
            {
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("permission", IdentityPermissions.KnowledgeBaseRead) ||
                    ctx.User.IsInRole(IdentityRoles.Customer) ||
                    ctx.User.IsInRole(IdentityRoles.Agent) ||
                    ctx.User.IsInRole(IdentityRoles.TenantAdmin) ||
                    ctx.User.IsInRole(IdentityRoles.SuperAdmin));
            });

            options.AddPolicy("kb:manage", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("permission", IdentityPermissions.KnowledgeBaseManage) ||
                    ctx.User.IsInRole(IdentityRoles.Agent) ||
                    ctx.User.IsInRole(IdentityRoles.TenantAdmin) ||
                    ctx.User.IsInRole(IdentityRoles.SuperAdmin));
            });
        });

        return services;
    }

    public static WebApplication UseKnowledgeBaseModule(this WebApplication app)
    {
        app.MapKnowledgeBaseEndpoints();
        return app;
    }
}
