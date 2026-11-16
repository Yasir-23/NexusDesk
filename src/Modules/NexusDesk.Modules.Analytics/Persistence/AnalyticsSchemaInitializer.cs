using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Analytics.Persistence;

public interface IAnalyticsSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class AnalyticsSchemaInitializer : IAnalyticsSchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AnalyticsSchemaInitializer(
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

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654332);", connection))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await EnsureAnalyticsSchemaAndRlsAsync(connection, cancellationToken);
            await MigrateDedicatedTenantDatabasesAsync(connection, cancellationToken);
            await SeedInitialDataAsync(connection, cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654332);", connection);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch
            {
                // Ignored
            }
        }
    }

    private static async Task EnsureAnalyticsSchemaAndRlsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS analytics;

            CREATE TABLE IF NOT EXISTS analytics.audit_log_entries (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                actor_user_id uuid NULL,
                actor_email varchar(256) NULL,
                action varchar(100) NOT NULL,
                resource_type varchar(100) NOT NULL,
                resource_id varchar(100) NOT NULL,
                metadata_json text NULL,
                ip_address varchar(64) NULL,
                created_at_utc timestamptz NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_audit_log_entries_tenant_created 
                ON analytics.audit_log_entries (tenant_id, created_at_utc DESC);

            CREATE INDEX IF NOT EXISTS ix_audit_log_entries_tenant_resource 
                ON analytics.audit_log_entries (tenant_id, resource_type, resource_id);

            CREATE TABLE IF NOT EXISTS analytics.user_notifications (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                recipient_user_id uuid NOT NULL,
                type varchar(50) NOT NULL,
                title varchar(200) NOT NULL,
                message varchar(1000) NOT NULL,
                link_url varchar(500) NULL,
                is_read boolean NOT NULL DEFAULT false,
                read_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_user_notifications_tenant_user_read_created 
                ON analytics.user_notifications (tenant_id, recipient_user_id, is_read, created_at_utc DESC);

            ALTER TABLE analytics.audit_log_entries ENABLE ROW LEVEL SECURITY;
            ALTER TABLE analytics.audit_log_entries FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation_policy ON analytics.audit_log_entries;
            CREATE POLICY tenant_isolation_policy ON analytics.audit_log_entries
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

            ALTER TABLE analytics.user_notifications ENABLE ROW LEVEL SECURITY;
            ALTER TABLE analytics.user_notifications FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation_policy ON analytics.user_notifications;
            CREATE POLICY tenant_isolation_policy ON analytics.user_notifications
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedInitialDataAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        // 1. Query seeded tenants (e.g., acme, beta)
        var tenants = new List<(Guid Id, string Subdomain)>();
        try
        {
            const string querySql = "SELECT id, subdomain FROM tenants.tenants WHERE subdomain IN ('acme', 'beta');";
            await using var cmd = new NpgsqlCommand(querySql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tenants.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }
        catch
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var (tenantId, subdomain) in tenants)
        {
            // Seed sample audit log entries if empty
            const string checkAuditSql = "SELECT COUNT(*) FROM analytics.audit_log_entries WHERE tenant_id = @tid;";
            await using (var checkCmd = new NpgsqlCommand(checkAuditSql, connection))
            {
                checkCmd.Parameters.AddWithValue("tid", tenantId);
                var count = (long)(await checkCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
                if (count == 0)
                {
                    const string insertAuditSql = """
                        INSERT INTO analytics.audit_log_entries 
                        (id, tenant_id, actor_user_id, actor_email, action, resource_type, resource_id, metadata_json, ip_address, created_at_utc)
                        VALUES 
                        (@id1, @tid, NULL, @email, 'tenant.provisioned', 'tenant', @tid_str, '{"subdomain":"'||@subdomain||'"}', '127.0.0.1', @now1),
                        (@id2, @tid, NULL, @email, 'billing.plan_changed', 'subscription', 'sub_001', '{"plan":"Pro"}', '127.0.0.1', @now2),
                        (@id3, @tid, NULL, @email, 'kb.article_published', 'kb_article', 'art_001', '{"title":"Welcome to Helpdesk"}', '127.0.0.1', @now3),
                        (@id4, @tid, NULL, @email, 'automation.rule_created', 'automation_rule', 'rule_001', '{"rule":"High Priority Escalation"}', '127.0.0.1', @now4);
                        """;

                    await using var insertCmd = new NpgsqlCommand(insertAuditSql, connection);
                    insertCmd.Parameters.AddWithValue("id1", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("id2", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("id3", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("id4", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("tid", tenantId);
                    insertCmd.Parameters.AddWithValue("tid_str", tenantId.ToString());
                    insertCmd.Parameters.AddWithValue("subdomain", subdomain);
                    insertCmd.Parameters.AddWithValue("email", $"admin@{subdomain}.com");
                    insertCmd.Parameters.AddWithValue("now1", now.AddDays(-5));
                    insertCmd.Parameters.AddWithValue("now2", now.AddDays(-4));
                    insertCmd.Parameters.AddWithValue("now3", now.AddDays(-3));
                    insertCmd.Parameters.AddWithValue("now4", now.AddDays(-2));
                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            // Find users in this tenant to receive initial notifications
            var usersToNotify = new List<Guid>();
            var acmeTenantId = Guid.Parse("b7e43bc4-a5c7-4c8f-a470-7277ce9823f4");
            var acmeAdminId = Guid.Parse("22222222-2222-2222-2222-222222222221");
            if (tenantId == acmeTenantId)
            {
                usersToNotify.Add(acmeAdminId);
            }

            try
            {
                const string queryUserSql = "SELECT id FROM identity.users WHERE tenant_id = @tid AND role IN ('TenantAdmin', 'Agent');";
                await using var userCmd = new NpgsqlCommand(queryUserSql, connection);
                userCmd.Parameters.AddWithValue("tid", tenantId);
                await using var reader = await userCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var uid = reader.GetGuid(0);
                    if (!usersToNotify.Contains(uid))
                    {
                        usersToNotify.Add(uid);
                    }
                }
            }
            catch
            {
                // Identity table might not be seeded yet
            }

            foreach (var userId in usersToNotify)
            {
                const string checkNotifSql = "SELECT COUNT(*) FROM analytics.user_notifications WHERE tenant_id = @tid AND recipient_user_id = @uid;";
                await using var checkNotifCmd = new NpgsqlCommand(checkNotifSql, connection);
                checkNotifCmd.Parameters.AddWithValue("tid", tenantId);
                checkNotifCmd.Parameters.AddWithValue("uid", userId);
                var notifCount = (long)(await checkNotifCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
                if (notifCount == 0)
                {
                    const string insertNotifSql = """
                        INSERT INTO analytics.user_notifications
                        (id, tenant_id, recipient_user_id, type, title, message, link_url, is_read, read_at_utc, created_at_utc)
                        VALUES
                        (@id1, @tid, @uid, 'billing_alert', 'Billing Cycle Activated', 'Your subscription period has begun.', '/billing', false, NULL, @now1),
                        (@id2, @tid, @uid, 'sla_warning', 'SLA Target Reminder', 'Tickets with high priority require response within 60 minutes.', '/tickets', false, NULL, @now2);
                        """;

                    await using var insertCmd = new NpgsqlCommand(insertNotifSql, connection);
                    insertCmd.Parameters.AddWithValue("id1", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("id2", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("tid", tenantId);
                    insertCmd.Parameters.AddWithValue("uid", userId);
                    insertCmd.Parameters.AddWithValue("now1", now.AddHours(-12));
                    insertCmd.Parameters.AddWithValue("now2", now.AddHours(-2));
                    await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
    }

    private static async Task MigrateDedicatedTenantDatabasesAsync(NpgsqlConnection defaultConn, CancellationToken cancellationToken)
    {
        var dedicatedTenants = new List<(string Subdomain, string? CustomConnectionString)>();
        try
        {
            const string sql = """
                SELECT subdomain, custom_connection_string
                FROM tenants.tenants
                WHERE isolation_strategy = 'DedicatedDb'
                  AND custom_connection_string IS NOT NULL;
                """;

            await using var cmd = new NpgsqlCommand(sql, defaultConn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var subdomain = reader.GetString(0);
                var connStr = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(connStr))
                {
                    dedicatedTenants.Add((subdomain, connStr));
                }
            }
        }
        catch
        {
            return;
        }

        foreach (var (_, customConnStr) in dedicatedTenants)
        {
            if (string.IsNullOrWhiteSpace(customConnStr))
            {
                continue;
            }

            try
            {
                await using var dedicatedConn = new NpgsqlConnection(customConnStr);
                await dedicatedConn.OpenAsync(cancellationToken);
                await EnsureAnalyticsSchemaAndRlsAsync(dedicatedConn, cancellationToken);
            }
            catch
            {
                // Ignore transient dedicated DB connection errors in test environment
            }
        }
    }
}
