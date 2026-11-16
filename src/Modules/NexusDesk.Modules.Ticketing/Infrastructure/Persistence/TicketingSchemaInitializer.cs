using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Ticketing.Infrastructure.Persistence;

public interface ITicketingSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class TicketingSchemaInitializer : ITicketingSchemaInitializer
{
    private readonly ITenantConnectionResolver _connectionResolver;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public TicketingSchemaInitializer(
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

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(987654323);", connection))
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
                await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(987654323);", connection);
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
        var optionsBuilder = new DbContextOptionsBuilder<TicketingDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ticketing");
            npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        await using var dbContext = new TicketingDbContext(optionsBuilder.Options, _tenantContextAccessor);
        await dbContext.Database.MigrateAsync(cancellationToken);

        const string attachmentsDdl = """
            CREATE TABLE IF NOT EXISTS ticketing.ticket_attachments (
                id uuid NOT NULL,
                tenant_id uuid NOT NULL,
                ticket_id uuid NOT NULL,
                uploaded_by_user_id uuid NOT NULL,
                file_name character varying(255) NOT NULL,
                content_type character varying(100) NOT NULL,
                size_bytes bigint NOT NULL,
                storage_path character varying(500) NOT NULL,
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT pk_ticket_attachments PRIMARY KEY (id),
                CONSTRAINT fk_ticket_attachments_tickets_ticket_id FOREIGN KEY (ticket_id) REFERENCES ticketing.tickets (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_ticket_attachments_tenant_ticket ON ticketing.ticket_attachments (tenant_id, ticket_id);

            ALTER TABLE ticketing.ticket_attachments ENABLE ROW LEVEL SECURITY;
            ALTER TABLE ticketing.ticket_attachments FORCE ROW LEVEL SECURITY;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_policies 
                    WHERE schemaname = 'ticketing' 
                      AND tablename = 'ticket_attachments' 
                      AND policyname = 'tenant_isolation_policy'
                ) THEN
                    CREATE POLICY tenant_isolation_policy ON ticketing.ticket_attachments
                        USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                        WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                END IF;
            END $$;
            """;
        await dbContext.Database.ExecuteSqlRawAsync(attachmentsDdl, cancellationToken);
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
