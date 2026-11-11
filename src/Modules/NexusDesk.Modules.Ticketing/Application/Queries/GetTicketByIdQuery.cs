using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Queries;

public sealed record GetTicketByIdQuery(
    Guid TicketId,
    Guid CallerUserId,
    bool IsCustomer);

public sealed class GetTicketByIdQueryHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public GetTicketByIdQueryHandler(
        TicketingDbContext ticketingDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _ticketingDb = ticketingDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<Result<TicketDetailsResponse>> HandleAsync(
        GetTicketByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.TenantNotResolved",
                "A valid active tenant is required."));
        }

        var ticket = await _ticketingDb.Tickets
            .AsNoTracking()
            .AsSplitQuery()
            .Include(t => t.Comments)
            .Include(t => t.Tags)
            .Include(t => t.ActivityLogs)
            .FirstOrDefaultAsync(t => t.Id == query.TicketId, cancellationToken);

        if (ticket == null)
        {
            return Result.Failure<TicketDetailsResponse>(Error.NotFound(
                "Ticket.NotFound",
                $"Ticket '{query.TicketId}' was not found."));
        }

        if (query.IsCustomer && ticket.CustomerId != query.CallerUserId)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Forbidden(
                "Ticket.AccessDenied",
                "Customers can only view their own tickets."));
        }

        return Result.Success(ticket.ToDetailsResponse(query.IsCustomer));
    }
}
