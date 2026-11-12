using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.Modules.Ticketing.Application.Commands;
using NexusDesk.Modules.Ticketing.Application.Queries;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.Modules.Ticketing.Endpoints;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.Modules.Ticketing.RealTime;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing;

public sealed class TicketingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Ticketing";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTicketingModule(configuration);
    }
}

public static class TicketingModuleExtensions
{
    public static IServiceCollection AddTicketingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITicketingSchemaInitializer, TicketingSchemaInitializer>();
        services.AddScoped<ITicketNumberGenerator, TicketNumberGenerator>();

        services.AddDbContext<TicketingDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ticketing");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .AddInterceptors(
                sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        // CQRS Command Handlers
        services.AddScoped<CreateTicketCommandHandler>();
        services.AddScoped<AssignTicketCommandHandler>();
        services.AddScoped<UpdateTicketStatusCommandHandler>();
        services.AddScoped<UpdateTicketPriorityCommandHandler>();
        services.AddScoped<AddTicketCommentCommandHandler>();

        // CQRS Query Handlers
        services.AddScoped<GetTicketByIdQueryHandler>();
        services.AddScoped<GetTicketsPagedQueryHandler>();

        // Real-Time Presence & Domain Event Dispatching
        services.AddSingleton<ITicketPresenceTracker, InMemoryTicketPresenceTracker>();
        services.AddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();
        services.AddScoped<IDomainEventHandler<TicketCreatedDomainEvent>, TicketRealtimeNotificationHandler>();
        services.AddScoped<IDomainEventHandler<TicketAssignedDomainEvent>, TicketRealtimeNotificationHandler>();
        services.AddScoped<IDomainEventHandler<TicketStatusChangedDomainEvent>, TicketRealtimeNotificationHandler>();
        services.AddScoped<IDomainEventHandler<TicketPriorityChangedDomainEvent>, TicketRealtimeNotificationHandler>();
        services.AddScoped<IDomainEventHandler<TicketCommentAddedDomainEvent>, TicketRealtimeNotificationHandler>();

        return services;
    }

    public static WebApplication UseTicketingModule(this WebApplication app)
    {
        app.MapTicketingEndpoints();
        return app;
    }
}
