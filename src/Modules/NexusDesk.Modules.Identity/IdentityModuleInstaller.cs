using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

namespace NexusDesk.Modules.Identity;

public sealed class IdentityModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Identity";

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityModule(configuration);
    }
}

public static class IdentityModuleExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Register Identity module services
        return services;
    }
}
