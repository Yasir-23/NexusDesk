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

        await using var dbContext = new TicketingDbContext(optionsBuilder.Options, _tenantContextAccessor);
        await dbContext.Database.MigrateAsync(cancellationToken);
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
