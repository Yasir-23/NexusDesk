using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Tenants.Persistence;

public interface ITenantConnectionResolver
{
    string GetConnectionString(ITenantContext tenantContext);
    string GetDefaultConnectionString();
}

public sealed class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly IConfiguration _configuration;
    private readonly IDataProtector _protector;
    public const string DataProtectionPurpose = "NexusDesk.TenantDedicatedConnection";

    public TenantConnectionResolver(IConfiguration configuration, IDataProtectionProvider dataProtectionProvider)
    {
        _configuration = configuration;
        _protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
    }

    public string GetDefaultConnectionString()
    {
        var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var postgresPort = Environment.GetEnvironmentVariable("NEXUSDESK_POSTGRES_PORT") ?? "56132";
        var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
        var postgresPass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
        var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "nexusdesk_shared";

        var defaultPgConn = $"Host={postgresHost};Port={postgresPort};Database={postgresDb};Username={postgresUser};Password={postgresPass}";
        return _configuration.GetConnectionString("PostgreSql") ?? defaultPgConn;
    }

    public string GetConnectionString(ITenantContext tenantContext)
    {
        if (tenantContext.IsolationStrategy == IsolationStrategy.DedicatedDatabase &&
            !string.IsNullOrWhiteSpace(tenantContext.CustomConnectionString))
        {
            try
            {
                return _protector.Unprotect(tenantContext.CustomConnectionString);
            }
            catch
            {
                // Fallback if raw or protection key mismatch in test scenarios
                return tenantContext.CustomConnectionString;
            }
        }

        return GetDefaultConnectionString();
    }
}
