using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Chat.Persistence;

public interface IChatSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class ChatSchemaInitializer : IChatSchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public ChatSchemaInitializer(
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

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654327);", connection))
        {
            await lockCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await MigrateDatabaseAsync(defaultConnString, cancellationToken);
            await MigrateDedicatedTenantDatabasesAsync(connection, cancellationToken);
        }
        finally
        {
            try
            {
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654327);", connection);
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
        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "chat");
            npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        await using var dbContext = new ChatDbContext(optionsBuilder.Options, _tenantContextAccessor);
        await dbContext.Database.MigrateAsync(cancellationToken);
        await EnsureRlsPoliciesAsync(dbContext, cancellationToken);
    }

    private static async Task EnsureRlsPoliciesAsync(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        const string rlsSql = """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'chat' AND table_name = 'chat_sessions') THEN
                    ALTER TABLE chat.chat_sessions ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE chat.chat_sessions FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON chat.chat_sessions;
                    CREATE POLICY tenant_isolation_policy ON chat.chat_sessions
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;

                IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'chat' AND table_name = 'chat_messages') THEN
                    ALTER TABLE chat.chat_messages ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE chat.chat_messages FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation_policy ON chat.chat_messages;
                    CREATE POLICY tenant_isolation_policy ON chat.chat_messages
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
}
