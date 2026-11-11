using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Commands;

public sealed record UpdateTicketPriorityCommand(
    Guid TicketId,
    TicketPriority NewPriority,
    Guid PerformedByUserId,
    uint? RowVersion = null);

public sealed class UpdateTicketPriorityCommandHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public UpdateTicketPriorityCommandHandler(
        TicketingDbContext ticketingDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _ticketingDb = ticketingDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<Result<TicketDetailsResponse>> HandleAsync(
        UpdateTicketPriorityCommand command,
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
            .Include(t => t.Comments)
            .Include(t => t.Tags)
            .Include(t => t.ActivityLogs)
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);

        if (ticket == null)
        {
            return Result.Failure<TicketDetailsResponse>(Error.NotFound(
                "Ticket.NotFound",
                $"Ticket '{command.TicketId}' was not found."));
        }

        if (command.RowVersion.HasValue)
        {
            _ticketingDb.Entry(ticket).Property(t => t.RowVersion).OriginalValue = command.RowVersion.Value;
        }

        var result = ticket.ChangePriority(command.NewPriority, command.PerformedByUserId);
        if (result.IsFailure)
        {
            return Result.Failure<TicketDetailsResponse>(result.Error);
        }

        try
        {
            await _ticketingDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Conflict(
                "Ticket.ConcurrencyConflict",
                "The ticket was modified by another request. Please refresh and retry."));
        }

        return Result.Success(ticket.ToDetailsResponse(isCustomer: false));
    }
}
