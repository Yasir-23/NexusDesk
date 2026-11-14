using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Billing.Domain;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Billing.Persistence;

public interface IBillingSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class BillingSchemaInitializer : IBillingSchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public BillingSchemaInitializer(
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

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654329);", connection))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await MigrateDatabaseAsync(defaultConnString, cancellationToken);
            await MigrateDedicatedTenantDatabasesAsync(connection, cancellationToken);
            await SeedPlansAndTenantsAsync(defaultConnString, cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654329);", connection);
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
        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "billing");
            npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        await using var dbContext = new BillingDbContext(optionsBuilder.Options, _tenantContextAccessor);
        await dbContext.Database.MigrateAsync(cancellationToken);
        await EnsureRlsPoliciesAsync(dbContext, cancellationToken);
    }

    private static async Task EnsureRlsPoliciesAsync(BillingDbContext dbContext, CancellationToken cancellationToken)
    {
        const string setupSql = """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'billing' AND table_name = 'tenant_subscriptions') THEN
                    ALTER TABLE billing.tenant_subscriptions ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE billing.tenant_subscriptions FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON billing.tenant_subscriptions;
                    CREATE POLICY tenant_isolation_policy ON billing.tenant_subscriptions
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;

                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'billing' AND table_name = 'usage_meter_records') THEN
                    ALTER TABLE billing.usage_meter_records ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE billing.usage_meter_records FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON billing.usage_meter_records;
                    CREATE POLICY tenant_isolation_policy ON billing.usage_meter_records
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;

                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'billing' AND table_name = 'billing_invoices') THEN
                    ALTER TABLE billing.billing_invoices ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE billing.billing_invoices FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON billing.billing_invoices;
                    CREATE POLICY tenant_isolation_policy ON billing.billing_invoices
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;
            END $$;
            """;

        await dbContext.Database.ExecuteSqlRawAsync(setupSql, cancellationToken);
    }

    [AllowCrossTenantQuery("Schema initializer seeds default plans and provisions tenant subscriptions across tenants")]
    private async Task SeedPlansAndTenantsAsync(string connectionString, CancellationToken cancellationToken)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "billing");
        });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        await using var dbContext = new BillingDbContext(optionsBuilder.Options, _tenantContextAccessor);

        // 1. Seed standard subscription plans
        var existingPlans = await dbContext.SubscriptionPlans.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seedPlans = new[]
        {
            new SubscriptionPlan(
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                "Free",
                "Free Plan",
                monthlyPriceCents: 0,
                maxAgents: 2,
                maxMonthlyTickets: 100,
                maxMonthlyAiTokens: 50_000L,
                customBrandingEnabled: false,
                webhooksEnabled: false,
                slaEnabled: false,
                isActive: true),
            new SubscriptionPlan(
                Guid.Parse("10000000-0000-0000-0000-000000000002"),
                "Starter",
                "Starter Plan",
                monthlyPriceCents: 2900,
                maxAgents: 5,
                maxMonthlyTickets: 500,
                maxMonthlyAiTokens: 250_000L,
                customBrandingEnabled: false,
                webhooksEnabled: true,
                slaEnabled: false,
                isActive: true),
            new SubscriptionPlan(
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                "Pro",
                "Pro Plan",
                monthlyPriceCents: 9900,
                maxAgents: 15,
                maxMonthlyTickets: 2500,
                maxMonthlyAiTokens: 1_000_000L,
                customBrandingEnabled: true,
                webhooksEnabled: true,
                slaEnabled: true,
                isActive: true),
            new SubscriptionPlan(
                Guid.Parse("10000000-0000-0000-0000-000000000004"),
                "Enterprise",
                "Enterprise Plan",
                monthlyPriceCents: 49900,
                maxAgents: 1000,
                maxMonthlyTickets: 50_000,
                maxMonthlyAiTokens: 50_000_000L,
                customBrandingEnabled: true,
                webhooksEnabled: true,
                slaEnabled: true,
                isActive: true)
        };

        foreach (var plan in seedPlans)
        {
            if (!existingPlans.ContainsKey(plan.Code))
            {
                dbContext.SubscriptionPlans.Add(plan);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        // 2. Auto-provision subscriptions for existing tenants if missing
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tenants = new List<(Guid Id, string PlanTier)>();
        try
        {
            const string queryTenantsSql = "SELECT id, plan_tier FROM tenants.tenants;";
            await using var cmd = new NpgsqlCommand(queryTenantsSql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tid = reader.GetGuid(0);
                var planTier = reader.GetString(1);
                tenants.Add((tid, planTier));
            }
        }
        catch
        {
            // Table tenants may not exist yet
            return;
        }

        var existingSubscriptions = await dbContext.TenantSubscriptions
            .IgnoreQueryFilters()
            .ToDictionaryAsync(s => s.TenantId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var (tenantId, planTier) in tenants)
        {
            if (!existingSubscriptions.ContainsKey(tenantId))
            {
                var planCode = planTier.Equals("Enterprise", StringComparison.OrdinalIgnoreCase) ? "Enterprise"
                    : planTier.Equals("Starter", StringComparison.OrdinalIgnoreCase) ? "Starter"
                    : planTier.Equals("Free", StringComparison.OrdinalIgnoreCase) ? "Free"
                    : "Pro";

                var sub = new TenantSubscription(
                    Guid.NewGuid(),
                    tenantId,
                    planCode,
                    "Active",
                    "Monthly",
                    now,
                    now.AddDays(30),
                    stripeCustomerId: null,
                    stripeSubscriptionId: null,
                    cancelAtPeriodEnd: false,
                    updatedAtUtc: now);

                dbContext.TenantSubscriptions.Add(sub);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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
}
