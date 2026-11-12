using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Automation.Application;
using NexusDesk.Modules.Automation.Endpoints;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Automation;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Outbox;

namespace NexusDesk.Modules.Automation;

public sealed class AutomationModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Automation";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAutomationModule(configuration);
    }
}

public static class AutomationModuleExtensions
{
    public static IServiceCollection AddAutomationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAutomationSchemaInitializer, AutomationSchemaInitializer>();

        services.AddDbContext<AutomationDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "automation");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .AddInterceptors(
                sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        services.AddScoped<ISlaPolicyProvider, SlaPolicyProvider>();
        services.AddScoped<IAutomationRuleEngine, AutomationRuleEvaluator>();
        services.AddScoped<IOutboxEventEnqueuer, OutboxEventEnqueuer>();

#pragma warning disable EXTEXP0001
        services.AddHttpClient(OutboxWebhookDispatcherBackgroundService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.AddSingleton<SlaBreachEvaluatorBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<SlaBreachEvaluatorBackgroundService>());

        services.AddSingleton<OutboxWebhookDispatcherBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxWebhookDispatcherBackgroundService>());

        return services;
    }

    public static WebApplication UseAutomationModule(this WebApplication app)
    {
        app.MapAutomationEndpoints();
        return app;
    }
}
