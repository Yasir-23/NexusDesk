using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

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
        // Register Tenants services
        return services;
    }
}
