using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NexusDesk.SharedKernel.Modules;

public interface IModuleInstaller
{
    string ModuleName { get; }
    void Install(IServiceCollection services, IConfiguration configuration);
}

public interface IModuleRegistry
{
    IReadOnlyList<string> RegisteredModules { get; }
}

public class ModuleRegistry : IModuleRegistry
{
    private readonly List<string> _modules = [];

    public IReadOnlyList<string> RegisteredModules => _modules.AsReadOnly();

    public void RegisterModule(string moduleName)
    {
        if (!string.IsNullOrWhiteSpace(moduleName) && !_modules.Contains(moduleName))
        {
            _modules.Add(moduleName);
        }
    }
}
