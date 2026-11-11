using Microsoft.AspNetCore.Identity;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Security;
using NexusDesk.Modules.Tenants.Persistence;
using Npgsql;

namespace NexusDesk.Modules.Identity.Persistence;

public interface IIdentitySchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class IdentitySchemaInitializer : IIdentitySchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IPiiEncryptionService _piiEncryptionService;

    public IdentitySchemaInitializer(
        ITenantConnectionResolver connectionResolver,
        IPasswordHasherService passwordHasher,
        IPiiEncryptionService piiEncryptionService)
    {
        _connectionResolver = connectionResolver;
        _passwordHasher = passwordHasher;
        _piiEncryptionService = piiEncryptionService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var defaultConnString = _connectionResolver.GetDefaultConnectionString();

        await using var connection = new NpgsqlConnection(defaultConnString);
        await connection.OpenAsync(cancellationToken);

        // 1. Acquire advisory lock to serialize initialization
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654322);", connection))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            // 2. Initialize identity tables and RLS in shared DB
            await InitializeSchemaOnConnectionAsync(connection, cancellationToken);

            // 3. Seed Platform SuperAdmin and tenant seed accounts
            await SeedDefaultAccountsAsync(connection, cancellationToken);

            // 4. Also check if dedicated database exists for globex and initialize it if present
            await CheckAndInitializeDedicatedTenantDbAsync("globex", cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654322);", connection);
                await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch
            {
                // Ignored
            }
        }
    }

    public static async Task InitializeSchemaOnConnectionAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        var sql = """
            CREATE SCHEMA IF NOT EXISTS identity;

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

            -- Row-Level Security
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
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedDefaultAccountsAsync(NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        const string defaultPassword = "Password123!";

        // Bypass RLS for platform seeding by setting app.current_tenant_id to platform guid
        await using (var rlsCmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', '00000000-0000-0000-0000-000000000000', false);", conn))
        {
            await rlsCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // 1. Seed Platform SuperAdmin (tenant_id = Guid.Empty)
        await SeedUserIfNotExistsAsync(
            conn,
            userId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            tenantId: Guid.Empty,
            email: "superadmin@nexusdesk.com",
            fullName: "Platform Super Admin",
            role: IdentityRoles.SuperAdmin,
            password: defaultPassword,
            phone: "+1-800-555-0100",
            now: now,
            cancellationToken: cancellationToken);

        // 2. Query existing tenants in tenants.tenants table to seed their accounts
        var tenants = new List<(Guid Id, string Subdomain)>();
        try
        {
            var selectTenantsSql = "SELECT id, subdomain FROM tenants.tenants WHERE subdomain IN ('acme', 'beta');";
            await using var selectCmd = new NpgsqlCommand(selectTenantsSql, conn);
            await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tenants.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }
        catch
        {
            // tenants table may not exist yet
        }

        foreach (var (tId, subdomain) in tenants)
        {
            // Set session variable so RLS allows inserting for this tenant
            await using (var rlsCmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tId, false);", conn))
            {
                rlsCmd.Parameters.AddWithValue("tId", tId.ToString());
                await rlsCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (subdomain == "acme")
            {
                await SeedUserIfNotExistsAsync(conn, Guid.Parse("22222222-2222-2222-2222-222222222221"), tId, "admin@acme.com", "Acme Admin", IdentityRoles.TenantAdmin, defaultPassword, "+1-800-555-0201", now, cancellationToken);
                await SeedUserIfNotExistsAsync(conn, Guid.Parse("22222222-2222-2222-2222-222222222222"), tId, "agent@acme.com", "Acme Agent", IdentityRoles.Agent, defaultPassword, "+1-800-555-0202", now, cancellationToken);
                await SeedUserIfNotExistsAsync(conn, Guid.Parse("22222222-2222-2222-2222-222222222223"), tId, "customer@acme.com", "Acme Customer", IdentityRoles.Customer, defaultPassword, "+1-800-555-0203", now, cancellationToken);
            }
            else if (subdomain == "beta")
            {
                await SeedUserIfNotExistsAsync(conn, Guid.Parse("33333333-3333-3333-3333-333333333331"), tId, "admin@beta.com", "Beta Admin", IdentityRoles.TenantAdmin, defaultPassword, "+1-800-555-0301", now, cancellationToken);
            }
        }
    }

    private async Task CheckAndInitializeDedicatedTenantDbAsync(string subdomain, CancellationToken cancellationToken)
    {
        var dedicatedDbName = $"nexusdesk_tenant_{subdomain}";
        var defaultConnString = _connectionResolver.GetDefaultConnectionString();
        var builder = new NpgsqlConnectionStringBuilder(defaultConnString);

        // Check if database exists
        var adminBuilder = new NpgsqlConnectionStringBuilder(defaultConnString) { Database = "postgres" };
        string adminConnStr;
        try
        {
            await using var test = new NpgsqlConnection(adminBuilder.ConnectionString);
            await test.OpenAsync(cancellationToken);
            adminConnStr = adminBuilder.ConnectionString;
        }
        catch
        {
            adminConnStr = defaultConnString;
        }

        bool exists = false;
        try
        {
            await using var adminConn = new NpgsqlConnection(adminConnStr);
            await adminConn.OpenAsync(cancellationToken);
            var checkSql = "SELECT 1 FROM pg_database WHERE datname = @name;";
            await using var checkCmd = new NpgsqlCommand(checkSql, adminConn);
            checkCmd.Parameters.AddWithValue("name", dedicatedDbName);
            exists = await checkCmd.ExecuteScalarAsync(cancellationToken) != null;
        }
        catch
        {
            exists = false;
        }

        if (!exists) return;

        // Query globex tenant ID from shared catalog
        Guid? globexTenantId = null;
        try
        {
            await using var catalogConn = new NpgsqlConnection(defaultConnString);
            await catalogConn.OpenAsync(cancellationToken);
            var getTenantSql = "SELECT id FROM tenants.tenants WHERE subdomain = @sub;";
            await using var getCmd = new NpgsqlCommand(getTenantSql, catalogConn);
            getCmd.Parameters.AddWithValue("sub", subdomain);
            var scalar = await getCmd.ExecuteScalarAsync(cancellationToken);
            if (scalar != null && scalar != DBNull.Value)
            {
                globexTenantId = (Guid)scalar;
            }
        }
        catch
        {
            // Ignored
        }

        if (globexTenantId == null) return;

        var dedicatedBuilder = new NpgsqlConnectionStringBuilder(defaultConnString) { Database = dedicatedDbName };
        await using var dedicatedConn = new NpgsqlConnection(dedicatedBuilder.ConnectionString);
        await dedicatedConn.OpenAsync(cancellationToken);

        await InitializeSchemaOnConnectionAsync(dedicatedConn, cancellationToken);

        await using (var rlsCmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tId, false);", dedicatedConn))
        {
            rlsCmd.Parameters.AddWithValue("tId", globexTenantId.Value.ToString());
            await rlsCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await SeedUserIfNotExistsAsync(
            dedicatedConn,
            userId: Guid.Parse("44444444-4444-4444-4444-444444444441"),
            tenantId: globexTenantId.Value,
            email: "admin@globex.com",
            fullName: "Globex Admin",
            role: IdentityRoles.TenantAdmin,
            password: "Password123!",
            phone: "+1-800-555-0401",
            now: DateTimeOffset.UtcNow,
            cancellationToken: cancellationToken);
    }

    private async Task SeedUserIfNotExistsAsync(
        NpgsqlConnection conn,
        Guid userId,
        Guid tenantId,
        string email,
        string fullName,
        string role,
        string password,
        string phone,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var checkUserSql = "SELECT id FROM identity.users WHERE tenant_id = @tenantId AND normalized_email = @email;";
        await using var checkCmd = new NpgsqlCommand(checkUserSql, conn);
        checkCmd.Parameters.AddWithValue("tenantId", tenantId);
        checkCmd.Parameters.AddWithValue("email", normalizedEmail);
        var existingUserId = await checkCmd.ExecuteScalarAsync(cancellationToken);

        if (existingUserId != null && existingUserId != DBNull.Value)
        {
            return;
        }

        var dummyUser = new User(userId, tenantId, email, fullName, role, string.Empty, phone, now);
        var passwordHash = _passwordHasher.HashPassword(dummyUser, password);
        var encryptedPhone = _piiEncryptionService.Encrypt(phone);

        var insertUserSql = """
            INSERT INTO identity.users (
                id, tenant_id, email, normalized_email, password_hash, full_name,
                encrypted_phone_number, role, is_active, is_anonymized, created_at_utc
            ) VALUES (
                @id, @tenantId, @email, @normalizedEmail, @passwordHash, @fullName,
                @encryptedPhone, @role, true, false, @createdAtUtc
            ) ON CONFLICT (tenant_id, normalized_email) DO NOTHING;

            INSERT INTO identity.user_role_assignments (id, user_id, tenant_id, role, created_at_utc)
            VALUES (@roleId, @id, @tenantId, @role, @createdAtUtc)
            ON CONFLICT (user_id, role) DO NOTHING;
            """;

        await using var insertCmd = new NpgsqlCommand(insertUserSql, conn);
        insertCmd.Parameters.AddWithValue("id", userId);
        insertCmd.Parameters.AddWithValue("tenantId", tenantId);
        insertCmd.Parameters.AddWithValue("email", email);
        insertCmd.Parameters.AddWithValue("normalizedEmail", normalizedEmail);
        insertCmd.Parameters.AddWithValue("passwordHash", passwordHash);
        insertCmd.Parameters.AddWithValue("fullName", fullName);
        insertCmd.Parameters.AddWithValue("encryptedPhone", (object?)encryptedPhone ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("role", role);
        insertCmd.Parameters.AddWithValue("createdAtUtc", now);
        insertCmd.Parameters.AddWithValue("roleId", Guid.NewGuid());

        await insertCmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
