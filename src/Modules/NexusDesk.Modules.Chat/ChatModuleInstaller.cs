using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Modules;

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
        // Register Chat module services
        return services;
    }
}
