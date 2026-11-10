using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

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
        // Register Ticketing module services
        return services;
    }
}
