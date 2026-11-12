using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Persistence;
using NexusDesk.Modules.Identity.Security;
using NexusDesk.Modules.Identity.Tokens;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.Modules;
using NexusDesk.SharedKernel.MultiTenancy;

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
        // 1. Core Security & Encryption Services
        services.AddDataProtection();
        services.AddSingleton<IPiiEncryptionService, PiiEncryptionService>();
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // 2. Schema Initializer
        services.AddScoped<IIdentitySchemaInitializer, IdentitySchemaInitializer>();

        // 3. Identity DbContext (supports hybrid database routing via ITenantConnectionResolver)
        services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            var resolver = sp.GetRequiredService<ITenantConnectionResolver>();
            var accessor = sp.GetRequiredService<ITenantContextAccessor>();
            var connStr = resolver.GetConnectionString(accessor.TenantContext);

            options.UseNpgsql(connStr)
                   .AddInterceptors(
                       sp.GetRequiredService<TenantAuditSaveChangesInterceptor>(),
                       sp.GetRequiredService<PostgresRlsConnectionInterceptor>());
        });

        // 4. JWT Bearer Authentication
        var keyString = configuration["Jwt:SigningKey"] ?? JwtTokenService.DefaultSecretKey;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
        var issuer = configuration["Jwt:Issuer"] ?? "https://nexusdesk.local";
        var audience = configuration["Jwt:Audience"] ?? "nexusdesk-api";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(5)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

        // 5. Policy-Based Authorization & Custom Handlers
        services.AddScoped<IAuthorizationHandler, TenantBoundaryAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(IdentityPolicies.PlatformSuperAdminOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireRole(IdentityRoles.SuperAdmin);
            });

            options.AddPolicy(IdentityPolicies.TenantAdminOrHigher, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new TenantBoundaryRequirement(IdentityRoles.TenantAdmin));
            });

            options.AddPolicy(IdentityPolicies.AgentOrHigher, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new TenantBoundaryRequirement(IdentityRoles.Agent));
            });

            options.AddPolicy(IdentityPolicies.AuthenticatedTenantUser, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new TenantBoundaryRequirement(IdentityRoles.Customer));
            });
        });

        return services;
    }
}
