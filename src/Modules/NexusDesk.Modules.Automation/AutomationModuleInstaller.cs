using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

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
        // Register Automation module services
        return services;
    }
}
