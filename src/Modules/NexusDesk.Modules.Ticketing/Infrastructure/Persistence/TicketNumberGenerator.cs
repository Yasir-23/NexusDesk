using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NexusDesk.Modules.Ticketing.Infrastructure.Persistence;

public interface ITicketNumberGenerator
{
    Task<string> NextTicketNumberAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed class TicketNumberGenerator : ITicketNumberGenerator
{
    private readonly TicketingDbContext _dbContext;

    public TicketNumberGenerator(TicketingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextTicketNumberAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID must be non-empty.", nameof(tenantId));
        }

        var conn = _dbContext.Database.GetDbConnection();
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ticketing.ticket_sequences (tenant_id, last_number)
                VALUES (@tenantId, 1001)
                ON CONFLICT (tenant_id)
                DO UPDATE SET last_number = ticketing.ticket_sequences.last_number + 1
                RETURNING last_number;
                """;

            var tenantParam = cmd.CreateParameter();
            tenantParam.ParameterName = "tenantId";
            tenantParam.Value = tenantId;
            cmd.Parameters.Add(tenantParam);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var nextNumber = Convert.ToInt64(result);
            return $"TICK-{nextNumber}";
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }
}
