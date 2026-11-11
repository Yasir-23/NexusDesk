using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace NexusDesk.Modules.Tenants.Persistence;

public record ProvisionedDatabaseResult(string DatabaseName, string RawConnectionString, string ProtectedConnectionString);

public interface ITenantDatabaseProvisioner
{
    Task<ProvisionedDatabaseResult> ProvisionDedicatedDatabaseAsync(string subdomain, CancellationToken cancellationToken = default);
}

public sealed class TenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly IDataProtector _protector;

    public TenantDatabaseProvisioner(
        ITenantConnectionResolver connectionResolver,
        IDataProtectionProvider dataProtectionProvider)
    {
        _connectionResolver = connectionResolver;
        _protector = dataProtectionProvider.CreateProtector(TenantConnectionResolver.DataProtectionPurpose);
    }

    public async Task<ProvisionedDatabaseResult> ProvisionDedicatedDatabaseAsync(
        string subdomain,
        CancellationToken cancellationToken = default)
    {
        var cleanSubdomain = subdomain.Trim().ToLowerInvariant();
        var databaseName = $"nexusdesk_tenant_{cleanSubdomain}";

        var defaultConnString = _connectionResolver.GetDefaultConnectionString();
        var builder = new NpgsqlConnectionStringBuilder(defaultConnString);

        // 1. Connect to default database (e.g. postgres or nexusdesk_shared) to create dedicated database
        var adminBuilder = new NpgsqlConnectionStringBuilder(defaultConnString)
        {
            Database = "postgres"
        };

        // Try connecting to 'postgres' first, fallback to current Database if postgres doesn't exist
        string adminConnStr;
        try
        {
            await using var testConn = new NpgsqlConnection(adminBuilder.ConnectionString);
            await testConn.OpenAsync(cancellationToken);
            adminConnStr = adminBuilder.ConnectionString;
        }
        catch
        {
            adminConnStr = defaultConnString;
        }

        await using (var adminConn = new NpgsqlConnection(adminConnStr))
        {
            await adminConn.OpenAsync(cancellationToken);

            var checkDbSql = "SELECT 1 FROM pg_database WHERE datname = @dbName;";
            await using var checkCmd = new NpgsqlCommand(checkDbSql, adminConn);
            checkCmd.Parameters.AddWithValue("dbName", databaseName);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken) != null;

            if (!exists)
            {
                // CREATE DATABASE cannot run in a transaction block
                var createDbSql = $"CREATE DATABASE \"{databaseName}\";";
                await using var createCmd = new NpgsqlCommand(createDbSql, adminConn);
                await createCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        // 2. Connect to the newly created dedicated database and initialize schemas & tables with RLS
        var dedicatedBuilder = new NpgsqlConnectionStringBuilder(defaultConnString)
        {
            Database = databaseName
        };
        var dedicatedConnString = dedicatedBuilder.ConnectionString;

        await using (var dedicatedConn = new NpgsqlConnection(dedicatedConnString))
        {
            await dedicatedConn.OpenAsync(cancellationToken);

            var initScript = """
                CREATE EXTENSION IF NOT EXISTS vector;
                CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

                CREATE SCHEMA IF NOT EXISTS tenants;
                CREATE SCHEMA IF NOT EXISTS identity;
                CREATE SCHEMA IF NOT EXISTS app;

                CREATE TABLE IF NOT EXISTS identity.users (
                    id uuid PRIMARY KEY,
                    tenant_id uuid NOT NULL,
                    email varchar(256) NOT NULL,
                    normalized_email varchar(256) NOT NULL,
                    password_hash text NOT NULL,
                    full_name varchar(150) NOT NULL,
                    encrypted_phone_number text NULL,
                    sso_provider varchar(50) NULL,
                    sso_subject_id varchar(200) NULL,
                    role varchar(30) NOT NULL,
                    is_active boolean NOT NULL DEFAULT true,
                    is_anonymized boolean NOT NULL DEFAULT false,
                    last_login_at_utc timestamptz NULL,
                    created_at_utc timestamptz NOT NULL DEFAULT now(),
                    updated_at_utc timestamptz NULL
                );

                CREATE INDEX IF NOT EXISTS ix_users_tenant_id ON identity.users (tenant_id);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_users_tenant_email ON identity.users (tenant_id, normalized_email);

                CREATE TABLE IF NOT EXISTS identity.user_role_assignments (
                    id uuid PRIMARY KEY,
                    user_id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    role varchar(30) NOT NULL,
                    created_at_utc timestamptz NOT NULL DEFAULT now()
                );

                CREATE INDEX IF NOT EXISTS ix_user_roles_tenant_id ON identity.user_role_assignments (tenant_id);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_user_roles_user_role ON identity.user_role_assignments (user_id, role);

                CREATE TABLE IF NOT EXISTS identity.refresh_tokens (
                    id uuid PRIMARY KEY,
                    user_id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    token_hash varchar(128) NOT NULL,
                    jwt_id varchar(64) NOT NULL,
                    expires_at_utc timestamptz NOT NULL,
                    revoked_at_utc timestamptz NULL,
                    replaced_by_token_hash varchar(128) NULL,
                    created_at_utc timestamptz NOT NULL DEFAULT now()
                );

                CREATE INDEX IF NOT EXISTS ix_refresh_tokens_tenant_id ON identity.refresh_tokens (tenant_id);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_refresh_tokens_token_hash ON identity.refresh_tokens (token_hash);

                CREATE TABLE IF NOT EXISTS identity.oauth_authorization_codes (
                    id uuid PRIMARY KEY,
                    code_hash varchar(128) NOT NULL,
                    user_id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    client_id varchar(100) NOT NULL,
                    redirect_uri varchar(500) NOT NULL,
                    code_challenge varchar(200) NOT NULL,
                    code_challenge_method varchar(20) NOT NULL,
                    scope varchar(200) NULL,
                    expires_at_utc timestamptz NOT NULL,
                    consumed_at_utc timestamptz NULL,
                    created_at_utc timestamptz NOT NULL DEFAULT now()
                );

                CREATE INDEX IF NOT EXISTS ix_oauth_codes_tenant_id ON identity.oauth_authorization_codes (tenant_id);
                CREATE UNIQUE INDEX IF NOT EXISTS ix_oauth_codes_code_hash ON identity.oauth_authorization_codes (code_hash);

                ALTER TABLE identity.users ENABLE ROW LEVEL SECURITY;
                ALTER TABLE identity.users FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON identity.users;
                CREATE POLICY tenant_isolation_policy ON identity.users
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid);

                ALTER TABLE identity.user_role_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE identity.user_role_assignments FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON identity.user_role_assignments;
                CREATE POLICY tenant_isolation_policy ON identity.user_role_assignments
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid);

                ALTER TABLE identity.refresh_tokens ENABLE ROW LEVEL SECURITY;
                ALTER TABLE identity.refresh_tokens FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON identity.refresh_tokens;
                CREATE POLICY tenant_isolation_policy ON identity.refresh_tokens
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid);

                ALTER TABLE identity.oauth_authorization_codes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE identity.oauth_authorization_codes FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON identity.oauth_authorization_codes;
                CREATE POLICY tenant_isolation_policy ON identity.oauth_authorization_codes
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid OR tenant_id = '00000000-0000-0000-0000-000000000000'::uuid);

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

            await using var initCmd = new NpgsqlCommand(initScript, dedicatedConn);
            await initCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var protectedConnString = _protector.Protect(dedicatedConnString);
        return new ProvisionedDatabaseResult(databaseName, dedicatedConnString, protectedConnString);
    }
}
