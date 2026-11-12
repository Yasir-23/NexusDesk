using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Automation.Persistence;

public interface IAutomationSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class AutomationSchemaInitializer : IAutomationSchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AutomationSchemaInitializer(
        ITenantConnectionResolver connectionResolver,
        ITenantContextAccessor tenantContextAccessor)
    {
        _connectionResolver = connectionResolver;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var defaultConnString = _connectionResolver.GetDefaultConnectionString();

        await using var connection = new NpgsqlConnection(defaultConnString);
        await connection.OpenAsync(cancellationToken);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654324);", connection))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await MigrateDatabaseAsync(defaultConnString, cancellationToken);
            await MigrateDedicatedTenantDatabasesAsync(connection, cancellationToken);
            await SeedDefaultSlaPoliciesForSeededTenantsAsync(connection, defaultConnString, cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654324);", connection);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch
            {
                // Ignored
            }
        }
    }

    private async Task MigrateDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AutomationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "automation");
            npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        });

        await using var dbContext = new AutomationDbContext(optionsBuilder.Options, _tenantContextAccessor);
        await dbContext.Database.MigrateAsync(cancellationToken);
        await EnsureRlsPoliciesAsync(dbContext, cancellationToken);
    }

    private static async Task EnsureRlsPoliciesAsync(AutomationDbContext dbContext, CancellationToken cancellationToken)
    {
        const string rlsSql = """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'automation' AND table_name = 'webhook_subscriptions') THEN
                    ALTER TABLE automation.webhook_subscriptions ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE automation.webhook_subscriptions FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON automation.webhook_subscriptions;
                    CREATE POLICY tenant_isolation_policy ON automation.webhook_subscriptions
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;

                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'automation' AND table_name = 'outbox_messages') THEN
                    ALTER TABLE automation.outbox_messages ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE automation.outbox_messages FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON automation.outbox_messages;
                    CREATE POLICY tenant_isolation_policy ON automation.outbox_messages
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;

                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'automation' AND table_name = 'dead_letter_webhooks') THEN
                    ALTER TABLE automation.dead_letter_webhooks ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE automation.dead_letter_webhooks FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON automation.dead_letter_webhooks;
                    CREATE POLICY tenant_isolation_policy ON automation.dead_letter_webhooks
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;
            END $$;
            """;
        await dbContext.Database.ExecuteSqlRawAsync(rlsSql, cancellationToken);
    }

    private async Task MigrateDedicatedTenantDatabasesAsync(NpgsqlConnection defaultConn, CancellationToken cancellationToken)
    {
        var dedicatedTenants = new List<(string Subdomain, string? CustomConnectionString)>();
        try
        {
            const string sql = """
                SELECT subdomain, custom_connection_string
                FROM tenants.tenants
                WHERE isolation_strategy = 'DedicatedDatabase';
                """;
            await using var cmd = new NpgsqlCommand(sql, defaultConn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var subdomain = reader.GetString(0);
                var customConn = reader.IsDBNull(1) ? null : reader.GetString(1);
                dedicatedTenants.Add((subdomain, customConn));
            }
        }
        catch
        {
            return;
        }

        var defaultConnStr = _connectionResolver.GetDefaultConnectionString();
        foreach (var (subdomain, customConn) in dedicatedTenants)
        {
            try
            {
                var targetConnStr = !string.IsNullOrWhiteSpace(customConn)
                    ? _connectionResolver.GetConnectionString(new TenantContext
                    {
                        TenantId = Guid.NewGuid(),
                        Subdomain = subdomain,
                        IsolationStrategy = IsolationStrategy.DedicatedDatabase,
                        CustomConnectionString = customConn
                    })
                    : new NpgsqlConnectionStringBuilder(defaultConnStr)
                    {
                        Database = $"nexusdesk_tenant_{subdomain}"
                    }.ConnectionString;

                await using var testConn = new NpgsqlConnection(targetConnStr);
                await testConn.OpenAsync(cancellationToken);
                await testConn.CloseAsync();

                await MigrateDatabaseAsync(targetConnStr, cancellationToken);
            }
            catch
            {
                // Dedicated DB may not be provisioned yet
            }
        }
    }

    private async Task SeedDefaultSlaPoliciesForSeededTenantsAsync(
        NpgsqlConnection defaultConn,
        string defaultConnStr,
        CancellationToken cancellationToken)
    {
        var seededTenants = new List<(Guid TenantId, string Subdomain, string IsolationStrategy, string? CustomConnectionString)>();
        try
        {
            const string sql = """
                SELECT id, subdomain, isolation_strategy, custom_connection_string
                FROM tenants.tenants
                WHERE subdomain IN ('acme', 'beta');
                """;
            await using var cmd = new NpgsqlCommand(sql, defaultConn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                seededTenants.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }
        catch
        {
            return;
        }

        var previousContext = _tenantContextAccessor.TenantContext;
        try
        {
            foreach (var (tenantId, subdomain, isolationStr, customConn) in seededTenants)
            {
                var isolation = Enum.TryParse<IsolationStrategy>(isolationStr, true, out var parsed)
                    ? parsed
                    : IsolationStrategy.SharedDatabase;

                var tenantContext = new TenantContext
                {
                    TenantId = tenantId,
                    Subdomain = subdomain,
                    IsolationStrategy = isolation,
                    CustomConnectionString = customConn,
                    Status = TenantStatus.Active
                };
                _tenantContextAccessor.TenantContext = tenantContext;

                var connStr = _connectionResolver.GetConnectionString(tenantContext);
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    connStr = defaultConnStr;
                }

                var optionsBuilder = new DbContextOptionsBuilder<AutomationDbContext>();
                optionsBuilder.UseNpgsql(connStr, npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "automation");
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                })
                .AddInterceptors(
                    new TenantAuditSaveChangesInterceptor(_tenantContextAccessor),
                    new PostgresRlsConnectionInterceptor(_tenantContextAccessor));

                await using var dbContext = new AutomationDbContext(optionsBuilder.Options, _tenantContextAccessor);
                var hasAny = await dbContext.SlaPolicies.AnyAsync(p => p.TenantId == tenantId, cancellationToken);
                if (hasAny)
                {
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var (priority, (firstResp, resolution)) in SlaPolicy.SystemDefaults)
                {
                    var policy = SlaPolicy.Create(tenantId, priority, firstResp, resolution, now);
                    if (policy.IsSuccess)
                    {
                        dbContext.SlaPolicies.Add(policy.Value);
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            _tenantContextAccessor.TenantContext = previousContext;
        }
    }
}
