using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Queries;

public sealed record GetTicketsPagedQuery(
    TicketStatus? Status,
    TicketPriority? Priority,
    Guid? AssignedAgentId,
    Guid? CustomerId,
    int Page,
    int PageSize,
    Guid CallerUserId,
    bool IsCustomer);

public sealed class GetTicketsPagedQueryHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public GetTicketsPagedQueryHandler(
        TicketingDbContext ticketingDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _ticketingDb = ticketingDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<Result<PagedTicketsResponse>> HandleAsync(
        GetTicketsPagedQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<PagedTicketsResponse>(Error.Validation(
                "Ticket.TenantNotResolved",
                "A valid active tenant is required."));
        }

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        var ticketsQuery = _ticketingDb.Tickets
            .AsNoTracking()
            .Include(t => t.Tags)
            .AsQueryable();

        if (query.IsCustomer)
        {
            ticketsQuery = ticketsQuery.Where(t => t.CustomerId == query.CallerUserId);
        }
        else if (query.CustomerId.HasValue)
        {
            ticketsQuery = ticketsQuery.Where(t => t.CustomerId == query.CustomerId.Value);
        }

        if (query.Status.HasValue)
        {
            ticketsQuery = ticketsQuery.Where(t => t.Status == query.Status.Value);
        }

        if (query.Priority.HasValue)
        {
            ticketsQuery = ticketsQuery.Where(t => t.Priority == query.Priority.Value);
        }

        if (query.AssignedAgentId.HasValue)
        {
            ticketsQuery = ticketsQuery.Where(t => t.AssignedAgentId == query.AssignedAgentId.Value);
        }

        var totalCount = await ticketsQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await ticketsQuery
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var responseItems = items.Select(t => t.ToSummaryResponse()).ToList();

        return Result.Success(new PagedTicketsResponse(
            responseItems,
            page,
            pageSize,
            totalCount,
            totalPages));
    }
}
