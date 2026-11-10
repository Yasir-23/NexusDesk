using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

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
        // Register KnowledgeBase module services
        return services;
    }
}
