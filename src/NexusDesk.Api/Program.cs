using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using NexusDesk.Api.GraphQL;
using NexusDesk.Api.Infrastructure;
using NexusDesk.Modules.Analytics;
using NexusDesk.Modules.Analytics.Persistence;
using NexusDesk.Modules.Automation;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.Modules.Billing;
using NexusDesk.Modules.Billing.Persistence;
using NexusDesk.Modules.Chat;
using NexusDesk.Modules.Chat.Persistence;
using NexusDesk.Modules.Identity;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Identity.Persistence;
using NexusDesk.Modules.KnowledgeBase;
using NexusDesk.Modules.KnowledgeBase.Persistence;
using NexusDesk.Modules.Tenants;
using NexusDesk.Modules.Tenants.Endpoints;
using NexusDesk.Modules.Tenants.Middleware;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Ticketing;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.Modules.Ticketing.RealTime;
using NexusDesk.ServiceDefaults;
using NexusDesk.SharedKernel.Modules;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Service Defaults (OpenTelemetry, Resilience, Health Checks, Service Discovery)
builder.AddServiceDefaults();

// 2. Exception Handling
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 3. OpenAPI / Swagger with JWT Bearer Security & Tenant Header Inputs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NexusDesk API",
        Version = "v1",
        Description = "NexusDesk Multi-Tenant Cloud-Native SaaS Helpdesk Platform"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter your JWT Bearer token as: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });

    c.OperationFilter<TenantHeaderSwaggerOperationFilter>();
});

// 3.1. CORS Policy for Frontend SPA & SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("NexusDeskCorsPolicy", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                return false;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host;
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".nexusdesk.local", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "nexusdesk.local", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".nexusdesk.com", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "nexusdesk.com", StringComparison.OrdinalIgnoreCase);
        })
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()
        .WithExposedHeaders("Content-Disposition");
    });
});

// 3.2. Tenant-Tiered Sliding Window Rate Limiter
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Type = "https://nexusdesk.io/errors/rate-limit-exceeded",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Rate limit quota exceeded for this tenant. Please retry after some time.",
            Instance = context.HttpContext.Request.Path
        };
        problem.Extensions["code"] = "RATE_LIMIT_EXCEEDED";
        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken: token);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var rateLimitEnabled = builder.Configuration.GetValue<bool?>("RateLimiting:Enabled") ?? false;
        if (!rateLimitEnabled)
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var tenantSubdomain = httpContext.Request.Headers["X-Tenant-Subdomain"].ToString();
        var partitionKey = !string.IsNullOrWhiteSpace(tenantSubdomain)
            ? $"tenant:{tenantSubdomain.Trim().ToLowerInvariant()}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        var tenantAccessor = httpContext.RequestServices.GetService<NexusDesk.SharedKernel.MultiTenancy.ITenantContextAccessor>();
        var planTier = tenantAccessor?.TenantContext?.PlanTier ?? NexusDesk.SharedKernel.MultiTenancy.TenantPlanTier.Free;

        var permitLimit = planTier switch
        {
            NexusDesk.SharedKernel.MultiTenancy.TenantPlanTier.Enterprise => builder.Configuration.GetValue<int?>("RateLimiting:EnterprisePermitLimit") ?? 2400,
            NexusDesk.SharedKernel.MultiTenancy.TenantPlanTier.Pro => builder.Configuration.GetValue<int?>("RateLimiting:ProPermitLimit") ?? 600,
            _ => builder.Configuration.GetValue<int?>("RateLimiting:FreePermitLimit") ?? 120
        };

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// 4. Database & Cache Connection Resolution
var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
var postgresPort = Environment.GetEnvironmentVariable("NEXUSDESK_POSTGRES_PORT") ?? "56132";
var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
var postgresPass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "nexusdesk_shared";

var defaultPgConn = $"Host={postgresHost};Port={postgresPort};Database={postgresDb};Username={postgresUser};Password={postgresPass};SSL Mode=Disable";
var pgConnectionString = builder.Configuration.GetConnectionString("PostgreSql") ?? defaultPgConn;

var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "127.0.0.1";
var redisPort = Environment.GetEnvironmentVariable("NEXUSDESK_REDIS_PORT") ?? "56179";
var defaultRedisConn = $"{redisHost}:{redisPort},abortConnect=false";
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? defaultRedisConn;

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var config = ConfigurationOptions.Parse(redisConnectionString);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});

// SignalR with JSON Enum Serialization & Optional Redis Backplane
var signalRBuilder = builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var useRedisBackplane = builder.Configuration.GetValue<bool>("SignalR:UseRedisBackplane");
if (useRedisBackplane && !string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("NexusDesk");
        options.Configuration.AbortOnConnectFail = false;
    });
}

// 5. Readiness Probes (PostgreSQL + vector extension probe & Redis ping)
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: pgConnectionString,
        healthQuery: "SELECT 1 FROM pg_extension WHERE extname = 'vector';",
        name: "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"])
    .AddRedis(
        redisConnectionString: redisConnectionString.Contains("abortConnect") ? redisConnectionString : $"{redisConnectionString},abortConnect=false",
        name: "redis",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]);

// 6. Register Modules
var moduleRegistry = new ModuleRegistry();
builder.Services.AddSingleton<IModuleRegistry>(moduleRegistry);

IModuleInstaller[] installers =
[
    new TenantsModuleInstaller(),
    new IdentityModuleInstaller(),
    new TicketingModuleInstaller(),
    new ChatModuleInstaller(),
    new KnowledgeBaseModuleInstaller(),
    new AutomationModuleInstaller(),
    new BillingModuleInstaller(),
    new AnalyticsModuleInstaller()
];

foreach (var installer in installers)
{
    installer.Install(builder.Services, builder.Configuration);
    moduleRegistry.RegisterModule(installer.ModuleName);
}

// 7. GraphQL API Server (HotChocolate)
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

var app = builder.Build();

// Run Tenant & Identity Schema Initializers
using (var scope = app.Services.CreateScope())
{
    var tenantInitializer = scope.ServiceProvider.GetService<ITenantSchemaInitializer>();
    if (tenantInitializer != null)
    {
        await tenantInitializer.InitializeAsync();
    }

    var identityInitializer = scope.ServiceProvider.GetService<IIdentitySchemaInitializer>();
    if (identityInitializer != null)
    {
        await identityInitializer.InitializeAsync();
    }

    var ticketingInitializer = scope.ServiceProvider.GetService<ITicketingSchemaInitializer>();
    if (ticketingInitializer != null)
    {
        await ticketingInitializer.InitializeAsync();
    }

    var automationInitializer = scope.ServiceProvider.GetService<IAutomationSchemaInitializer>();
    if (automationInitializer != null)
    {
        await automationInitializer.InitializeAsync();
    }

    var chatInitializer = scope.ServiceProvider.GetService<IChatSchemaInitializer>();
    if (chatInitializer != null)
    {
        await chatInitializer.InitializeAsync();
    }

    var kbInitializer = scope.ServiceProvider.GetService<IKnowledgeBaseSchemaInitializer>();
    if (kbInitializer != null)
    {
        await kbInitializer.InitializeAsync();
    }

    var billingInitializer = scope.ServiceProvider.GetService<IBillingSchemaInitializer>();
    if (billingInitializer != null)
    {
        await billingInitializer.InitializeAsync();
    }

    var analyticsInitializer = scope.ServiceProvider.GetService<IAnalyticsSchemaInitializer>();
    if (analyticsInitializer != null)
    {
        await analyticsInitializer.InitializeAsync();
    }
}

// Configure Middleware Pipeline
app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "NexusDesk API v1");
    c.RoutePrefix = "swagger";
});

// Map Default Health Check Endpoints (/health/live, /health/ready)
app.MapDefaultEndpoints();

// CORS & Rate Limiting Pipeline
app.UseCors("NexusDeskCorsPolicy");
app.UseRateLimiter();

// Authentication & Tenant Authorization Middleware Pipeline
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<TenantClaimMatchMiddleware>();
app.UseAuthorization();

// Identity & Auth REST APIs
app.MapIdentityEndpoints();

// Tenant Management & Verification REST APIs
app.MapTenantEndpoints();

// Core Ticketing Engine & State Machine REST APIs
app.UseTicketingModule();

// SLA Engine & Automation Rules REST APIs
app.UseAutomationModule();

// Omnichannel Live Chat Module & SignalR ChatHub
app.UseChatModule();

// Knowledge Base & Article Deflection REST APIs
app.UseKnowledgeBaseModule();

// Multi-Tenant SaaS Billing, Plan Quotas & Usage Metering REST APIs
app.UseBillingModule();

// Executive Analytics, Audit Logs & In-App Notifications REST APIs
app.UseAnalyticsModule();

// GraphQL API Endpoint (/graphql via HotChocolate)
app.MapGraphQL("/graphql");

// Real-Time SignalR Hubs
app.MapHub<TicketHub>("/hubs/tickets");

// Metadata & Diagnostic Endpoint
app.MapGet("/api/v1/system/info", async (
    IModuleRegistry registry,
    IConfiguration config,
    IWebHostEnvironment env,
    HealthCheckService healthCheckService) =>
{
    var healthReport = await healthCheckService.CheckHealthAsync();

    var postgresStatus = healthReport.Entries.TryGetValue("postgresql", out var pgEntry)
        ? pgEntry.Status.ToString()
        : "Unknown";

    var redisStatus = healthReport.Entries.TryGetValue("redis", out var rEntry)
        ? rEntry.Status.ToString()
        : "Unknown";

    return Results.Ok(new
    {
        application = "NexusDesk.Api",
        environment = env.EnvironmentName,
        framework = RuntimeInformation.FrameworkDescription,
        osVersion = RuntimeInformation.OSDescription,
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        loadedModules = registry.RegisteredModules,
        connectivity = new
        {
            overall = healthReport.Status.ToString(),
            postgresql = postgresStatus,
            redis = redisStatus
        },
        timestampUtc = DateTimeOffset.UtcNow
    });
})
.WithName("GetSystemInfo")
.WithOpenApi();

app.Run();

public partial class Program;
