using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Tokens;

namespace NexusDesk.IntegrationTests;

public static class AuthTestTokenHelper
{
    public static string CreateSuperAdminToken(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Empty,
            "superadmin@nexusdesk.com",
            "Platform Super Admin",
            IdentityRoles.SuperAdmin,
            "hash");
        return tokenService.GenerateTokenResponse(user, "platform", "Enterprise").access_token;
    }

    public static string CreateTenantAdminToken(WebApplicationFactory<Program> factory, Guid tenantId, string subdomain, string planTier = "Pro", Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User(
            userId ?? Guid.NewGuid(),
            tenantId,
            $"admin@{subdomain}.com",
            $"{subdomain} Admin",
            IdentityRoles.TenantAdmin,
            "hash");
        return tokenService.GenerateTokenResponse(user, subdomain, planTier).access_token;
    }

    public static string CreateAgentToken(WebApplicationFactory<Program> factory, Guid tenantId, string subdomain, string planTier = "Pro", Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User(
            userId ?? Guid.NewGuid(),
            tenantId,
            $"agent@{subdomain}.com",
            $"{subdomain} Agent",
            IdentityRoles.Agent,
            "hash");
        return tokenService.GenerateTokenResponse(user, subdomain, planTier).access_token;
    }

    public static string CreateCustomerToken(WebApplicationFactory<Program> factory, Guid tenantId, string subdomain, string planTier = "Pro", Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User(
            userId ?? Guid.NewGuid(),
            tenantId,
            $"customer@{subdomain}.com",
            $"{subdomain} Customer",
            IdentityRoles.Customer,
            "hash");
        return tokenService.GenerateTokenResponse(user, subdomain, planTier).access_token;
    }

    public static HttpClient CreateSuperAdminClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var token = CreateSuperAdminToken(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient CreateTenantAdminClient(WebApplicationFactory<Program> factory, Guid tenantId, string subdomain, string planTier = "Pro")
    {
        var client = factory.CreateClient();
        var token = CreateTenantAdminToken(factory, tenantId, subdomain, planTier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);
        return client;
    }
}
