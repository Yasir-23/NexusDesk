using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using NexusDesk.SharedKernel.Primitives;
using Xunit;

namespace NexusDesk.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly SharedKernelAssembly = typeof(ITenantScoped).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Modules.Tenants.TenantsModuleInstaller).Assembly,
        typeof(Modules.Identity.IdentityModuleInstaller).Assembly,
        typeof(Modules.Ticketing.TicketingModuleInstaller).Assembly,
        typeof(Modules.Chat.ChatModuleInstaller).Assembly,
        typeof(Modules.KnowledgeBase.KnowledgeBaseModuleInstaller).Assembly,
        typeof(Modules.Automation.AutomationModuleInstaller).Assembly,
        typeof(Modules.Billing.BillingModuleInstaller).Assembly
    ];

    [Fact]
    public void DomainModules_ShouldNotDirectlyReference_ApiHost()
    {
        foreach (var assembly in ModuleAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("NexusDesk.Api")
                .GetResult();

            result.IsSuccessful.Should().BeTrue($"Module '{assembly.GetName().Name}' must not reference NexusDesk.Api.");
        }
    }

    [Fact]
    public void SharedKernel_ShouldNotDependOn_AnyModuleOrInfrastructureHost()
    {
        var forbiddenDependencies = new[]
        {
            "NexusDesk.Api",
            "NexusDesk.Modules.Tenants",
            "NexusDesk.Modules.Identity",
            "NexusDesk.Modules.Ticketing",
            "NexusDesk.Modules.Chat",
            "NexusDesk.Modules.KnowledgeBase",
            "NexusDesk.Modules.Automation",
            "NexusDesk.Modules.Billing",
            "NexusDesk.ServiceDefaults",
            "NexusDesk.AppHost"
        };

        var result = Types.InAssembly(SharedKernelAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenDependencies)
            .GetResult();

        result.IsSuccessful.Should().BeTrue("SharedKernel must have zero dependencies on any module or infrastructure host.");
    }

    [Fact]
    public void ClassesImplementingITenantScoped_MustHaveNonNullableGuidTenantIdProperty()
    {
        var assembliesToScan = ModuleAssemblies.Append(SharedKernelAssembly);

        foreach (var assembly in assembliesToScan)
        {
            var tenantScopedTypes = assembly.GetTypes()
                .Where(t => typeof(ITenantScoped).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .ToList();

            foreach (var type in tenantScopedTypes)
            {
                var tenantIdProp = type.GetProperty("TenantId");
                tenantIdProp.Should().NotBeNull($"Class '{type.FullName}' implements ITenantScoped and must declare TenantId.");
                tenantIdProp!.PropertyType.Should().Be(typeof(Guid), $"Class '{type.FullName}' TenantId must be of type Guid.");
            }
        }
    }
}
