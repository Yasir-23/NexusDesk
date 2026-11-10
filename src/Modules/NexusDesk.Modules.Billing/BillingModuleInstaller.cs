using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

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
        // Register Billing module services
        return services;
    }
}
