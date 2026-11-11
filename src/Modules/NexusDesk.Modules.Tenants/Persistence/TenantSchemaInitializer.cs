using Npgsql;

namespace NexusDesk.Modules.Tenants.Persistence;

public interface ITenantSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class TenantSchemaInitializer : ITenantSchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;

    public TenantSchemaInitializer(ITenantConnectionResolver connectionResolver)
    {
        _connectionResolver = connectionResolver;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connStr = _connectionResolver.GetDefaultConnectionString();

        await using var connection = new NpgsqlConnection(connStr);
        await connection.OpenAsync(cancellationToken);

        // Acquire PostgreSQL advisory lock to serialize schema initialization across concurrent test hosts
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654321);", connection))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var sql = """
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

            CREATE SCHEMA IF NOT EXISTS identity;
            CREATE SCHEMA IF NOT EXISTS tenants;
            CREATE SCHEMA IF NOT EXISTS ticketing;
            CREATE SCHEMA IF NOT EXISTS knowledge;
            CREATE SCHEMA IF NOT EXISTS automation;
            CREATE SCHEMA IF NOT EXISTS billing;
            CREATE SCHEMA IF NOT EXISTS catalog;
            CREATE SCHEMA IF NOT EXISTS app;

            CREATE TABLE IF NOT EXISTS tenants.tenants (
                id uuid PRIMARY KEY,
                name varchar(120) NOT NULL,
                subdomain varchar(63) NOT NULL,
                plan_tier varchar(20) NOT NULL,
                isolation_strategy varchar(20) NOT NULL,
                custom_connection_string text NULL,
                status varchar(20) NOT NULL,
                max_agents int NOT NULL,
                max_tickets_per_month int NOT NULL,
                max_storage_bytes bigint NOT NULL,
                created_at_utc timestamptz NOT NULL,
                updated_at_utc timestamptz NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_tenants_subdomain ON tenants.tenants (subdomain);

            CREATE TABLE IF NOT EXISTS tenants.tenant_audit_logs (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                action varchar(80) NOT NULL,
                reason varchar(500) NOT NULL,
                performed_by varchar(150) NOT NULL,
                timestamp_utc timestamptz NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_tenant_audit_logs_tenant_id ON tenants.tenant_audit_logs (tenant_id);

            CREATE TABLE IF NOT EXISTS tenants.tenant_memberships (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                user_id uuid NOT NULL,
                role varchar(50) NOT NULL,
                email varchar(256) NOT NULL DEFAULT '',
                full_name varchar(150) NOT NULL DEFAULT '',
                created_at_utc timestamptz NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS ix_tenant_memberships_tenant_id ON tenants.tenant_memberships (tenant_id);

            ALTER TABLE tenants.tenant_memberships ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenants.tenant_memberships FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS tenant_isolation_policy ON tenants.tenant_memberships;
            CREATE POLICY tenant_isolation_policy ON tenants.tenant_memberships
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

            CREATE TABLE IF NOT EXISTS tenants.tenant_settings (
                id uuid PRIMARY KEY,
                tenant_id uuid NOT NULL,
                key varchar(100) NOT NULL,
                value text NOT NULL DEFAULT '',
                created_at_utc timestamptz NOT NULL DEFAULT now(),
                updated_at_utc timestamptz NULL
            );

            CREATE INDEX IF NOT EXISTS ix_tenant_settings_tenant_id ON tenants.tenant_settings (tenant_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_settings_tenant_key ON tenants.tenant_settings (tenant_id, key);

            ALTER TABLE tenants.tenant_settings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tenants.tenant_settings FORCE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS tenant_isolation_policy ON tenants.tenant_settings;
            CREATE POLICY tenant_isolation_policy ON tenants.tenant_settings
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """;

            await using var cmd = new NpgsqlCommand(sql, connection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // Concurrent extension creation collision handled
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654321);", connection);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch
            {
                // Ignored
            }
        }
    }
}
