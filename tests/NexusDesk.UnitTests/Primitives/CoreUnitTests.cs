using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Automation;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Billing;
using NexusDesk.Modules.Billing.Domain;
using NexusDesk.Modules.Chat;
using NexusDesk.Modules.Chat.Domain;
using NexusDesk.Modules.Identity;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.KnowledgeBase;
using NexusDesk.Modules.KnowledgeBase.Domain;
using NexusDesk.Modules.Tenants;
using NexusDesk.Modules.Tenants.Domain;
using NexusDesk.Modules.Ticketing;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.Primitives;
using NexusDesk.SharedKernel.Time;
using Xunit;

namespace NexusDesk.UnitTests.Primitives;

public class ErrorTests
{
    [Fact]
    public void ErrorFactoryMethods_ShouldAssignCorrectProperties()
    {
        var failure = Error.Failure("Code.Fail", "Failure desc");
        failure.Code.Should().Be("Code.Fail");
        failure.Description.Should().Be("Failure desc");
        failure.Type.Should().Be(ErrorType.Failure);

        var notFound = Error.NotFound("Code.NotFound", "Not found desc");
        notFound.Type.Should().Be(ErrorType.NotFound);

        var validation = Error.Validation("Code.Validation", "Validation desc");
        validation.Type.Should().Be(ErrorType.Validation);

        var conflict = Error.Conflict("Code.Conflict", "Conflict desc");
        conflict.Type.Should().Be(ErrorType.Conflict);

        var unauthorized = Error.Unauthorized("Code.Auth", "Unauthorized desc");
        unauthorized.Type.Should().Be(ErrorType.Unauthorized);

        var forbidden = Error.Forbidden("Code.Forbidden", "Forbidden desc");
        forbidden.Type.Should().Be(ErrorType.Forbidden);
    }
}

public class TenantScopedTests
{
    [Fact]
    public void DomainEntities_ImplementingITenantScoped_ShouldContainNonEmptyTenantId()
    {
        var tenantId = Guid.NewGuid();

        var user = new User(Guid.NewGuid(), tenantId, "user@test.com", "User Name", "Agent");
        user.TenantId.Should().Be(tenantId);

        var ticketResult = Ticket.Create(tenantId, "TICK-1001", "Subject", "Desc", TicketPriority.High, TicketChannel.WebPortal, Guid.NewGuid());
        ticketResult.IsSuccess.Should().BeTrue();
        var ticket = ticketResult.Value;
        ticket.TenantId.Should().Be(tenantId);

        var membership = new TenantMembership(Guid.NewGuid(), tenantId, user.Id, "Admin");
        membership.TenantId.Should().Be(tenantId);

        var chatMessage = new ChatMessage(Guid.NewGuid(), tenantId, ticket.Id, user.Id, "Hello");
        chatMessage.TenantId.Should().Be(tenantId);

        var kb = new KbArticle(Guid.NewGuid(), tenantId, "Title", "slug", "Content", true);
        kb.TenantId.Should().Be(tenantId);

        var rule = new AutomationRule(Guid.NewGuid(), tenantId, "Escalate", "ticket.created");
        rule.TenantId.Should().Be(tenantId);

        var invoice = new Invoice(Guid.NewGuid(), tenantId, "2026-11", 49.99m, "Pending");
        invoice.TenantId.Should().Be(tenantId);
    }
}

public class ModuleRegistryTests
{
    [Fact]
    public void ModuleRegistry_ShouldRegisterAndRetrieveAllSevenModules()
    {
        var registry = new ModuleRegistry();
        IModuleInstaller[] installers =
        [
            new TenantsModuleInstaller(),
            new IdentityModuleInstaller(),
            new TicketingModuleInstaller(),
            new ChatModuleInstaller(),
            new KnowledgeBaseModuleInstaller(),
            new AutomationModuleInstaller(),
            new BillingModuleInstaller()
        ];

        var services = new ServiceCollection();
        IConfiguration config = new FakeConfiguration();

        foreach (var installer in installers)
        {
            installer.ModuleName.Should().NotBeNullOrWhiteSpace();
            installer.Install(services, config);
            registry.RegisterModule(installer.ModuleName);
        }

        registry.RegisteredModules.Should().HaveCount(7);
        registry.RegisteredModules.Should().Contain(
        [
            "Tenants",
            "Identity",
            "Ticketing",
            "Chat",
            "KnowledgeBase",
            "Automation",
            "Billing"
        ]);
    }
}

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ShouldReturnCurrentTime()
    {
        var clock = new SystemClock();
        var now = clock.UtcNow;
        var diff = DateTimeOffset.UtcNow - now;

        diff.Duration().Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}

internal sealed class FakeConfiguration : IConfiguration
{
    public string? this[string key] { get => null; set { } }
    public IEnumerable<IConfigurationSection> GetChildren() => [];
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}
