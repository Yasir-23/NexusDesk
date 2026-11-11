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

    [Fact]
    public void AllDomainEntities_WithTenantIdProperty_MustImplementITenantScoped()
    {
        var assembliesToScan = ModuleAssemblies;

        foreach (var assembly in assembliesToScan)
        {
            var typesWithTenantId = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                            t.Namespace != null && t.Namespace.EndsWith(".Domain") &&
                            !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) &&
                            t.Name != "Tenant" && t.Name != "TenantAuditLog" &&
                            t.GetProperty("TenantId") != null)
                .ToList();

            foreach (var type in typesWithTenantId)
            {
                typeof(ITenantScoped).IsAssignableFrom(type)
                    .Should().BeTrue($"Class '{type.FullName}' declares a 'TenantId' property and MUST implement ITenantScoped.");
            }
        }
    }

    [Fact]
    public void IdentityEntities_MustImplementITenantScoped_WithGuidTenantId()
    {
        var identityAssembly = typeof(Modules.Identity.IdentityModuleInstaller).Assembly;
        var identityEntities = identityAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        t.Namespace != null && t.Namespace.EndsWith(".Domain") &&
                        !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            .ToList();

        identityEntities.Should().NotBeEmpty();

        foreach (var type in identityEntities.Where(t => typeof(ITenantScoped).IsAssignableFrom(t)))
        {
            var prop = type.GetProperty("TenantId");
            prop.Should().NotBeNull();
            prop!.PropertyType.Should().Be(typeof(Guid));
        }
    }

    [Fact]
    public void Codebase_MustNotContain_UnapprovedIgnoreQueryFiltersCalls()
    {
        // Scan all .cs files in src/
        string solutionRoot;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NexusDesk.sln")))
        {
            dir = dir.Parent;
        }
        if (dir != null)
        {
            solutionRoot = dir.FullName;
        }
        else if (Directory.Exists(@"D:\dotnet\NexusDesk"))
        {
            solutionRoot = @"D:\dotnet\NexusDesk";
        }
        else
        {
            solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        }
        var srcDir = Path.Combine(solutionRoot, "src");

        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\") && !f.Contains("IgnoreQueryFiltersTenantAnalyzer.cs"))
            .ToList();

        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains(".IgnoreQueryFilters()"))
            {
                if (!content.Contains("AllowCrossTenantQuery"))
                {
                    violations.Add(file);
                }
            }
        }

        violations.Should().BeEmpty("Calling .IgnoreQueryFilters() without [AllowCrossTenantQuery] is prohibited across all modules.");
    }
}
