using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.Ticketing;

namespace NexusDesk.Modules.Ticketing.Application.Services;

public sealed class TicketDataProvider : ITicketDataProvider
{
    private readonly TicketingDbContext _ticketingDb;

    public TicketDataProvider(TicketingDbContext ticketingDb)
    {
        _ticketingDb = ticketingDb;
    }

    public async Task<TicketAiContextDto?> GetTicketAiContextAsync(Guid tenantId, Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.Id == ticketId && t.TenantId == tenantId)
            .Select(t => new
            {
                t.Id,
                t.TenantId,
                t.TicketNumber,
                t.Title,
                t.Description
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket == null)
        {
            return null;
        }

        var comments = await _ticketingDb.TicketComments
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId && c.TenantId == tenantId)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => c.Body)
            .ToListAsync(cancellationToken);

        return new TicketAiContextDto(
            ticket.Id,
            ticket.TenantId,
            ticket.TicketNumber,
            ticket.Title,
            ticket.Description,
            comments);
    }
}
