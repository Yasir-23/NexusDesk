using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Chat.Endpoints;
using NexusDesk.Modules.Chat.Persistence;
using NexusDesk.Modules.Chat.RealTime;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Chat;

public sealed class ChatModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Chat";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddChatModule(configuration);
    }
}

public static class ChatModuleExtensions
{
    public static IServiceCollection AddChatModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IChatSchemaInitializer, ChatSchemaInitializer>();
        services.AddScoped<NexusDesk.SharedKernel.Analytics.IChatAnalyticsProvider, NexusDesk.Modules.Chat.Services.ChatAnalyticsProvider>();

        services.AddDbContext<ChatDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "chat");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(
                sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        return services;
    }

    public static WebApplication UseChatModule(this WebApplication app)
    {
        app.MapChatEndpoints();
        app.MapHub<ChatHub>("/hubs/chat");
        return app;
    }
}
