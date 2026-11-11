using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Commands;

public sealed record UpdateTicketStatusCommand(
    Guid TicketId,
    TicketStatus TargetStatus,
    string? ResolutionNotes,
    string? Reason,
    Guid PerformedByUserId,
    uint? RowVersion = null);

public sealed class UpdateTicketStatusCommandHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public UpdateTicketStatusCommandHandler(
        TicketingDbContext ticketingDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _ticketingDb = ticketingDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<Result<TicketDetailsResponse>> HandleAsync(
        UpdateTicketStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.TenantNotResolved",
                "A valid active tenant is required."));
        }

        if (!Enum.IsDefined(command.TargetStatus))
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.InvalidStatus",
                "The requested ticket status is invalid."));
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

        var transitionResult = command.TargetStatus switch
        {
            TicketStatus.Open => ticket.Status is TicketStatus.Resolved or TicketStatus.Closed
                ? ticket.Reopen(command.Reason ?? string.Empty, command.PerformedByUserId)
                : ticket.MoveToOpen(command.PerformedByUserId),
            TicketStatus.PendingCustomer => ticket.MoveToPendingCustomer(command.PerformedByUserId),
            TicketStatus.PendingInternal => ticket.MoveToPendingInternal(command.PerformedByUserId),
            TicketStatus.Resolved => ticket.Resolve(command.ResolutionNotes ?? string.Empty, command.PerformedByUserId),
            TicketStatus.Closed => ticket.Close(command.PerformedByUserId),
            TicketStatus.New => Result.Failure(Error.Validation(
                "Ticket.InvalidTransition",
                "Cannot transition a ticket back to New.")),
            _ => Result.Failure(Error.Validation("Ticket.InvalidStatus", "The requested ticket status is invalid."))
        };

        if (transitionResult.IsFailure)
        {
            return Result.Failure<TicketDetailsResponse>(transitionResult.Error);
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
