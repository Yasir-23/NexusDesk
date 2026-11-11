using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Commands;

public sealed record AddTicketCommentCommand(
    Guid TicketId,
    string Body,
    CommentType Type,
    Guid AuthorUserId,
    bool IsCustomer);

public sealed class AddTicketCommentCommandHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AddTicketCommentCommandHandler(
        TicketingDbContext ticketingDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _ticketingDb = ticketingDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<Result<TicketCommentDto>> HandleAsync(
        AddTicketCommentCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<TicketCommentDto>(Error.Validation(
                "Ticket.TenantNotResolved",
                "A valid active tenant is required."));
        }

        var ticket = await _ticketingDb.Tickets
            .Include(t => t.Comments)
            .Include(t => t.ActivityLogs)
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);

        if (ticket == null)
        {
            return Result.Failure<TicketCommentDto>(Error.NotFound(
                "Ticket.NotFound",
                $"Ticket '{command.TicketId}' was not found."));
        }

        if (command.IsCustomer && ticket.CustomerId != command.AuthorUserId)
        {
            return Result.Failure<TicketCommentDto>(Error.Forbidden(
                "Ticket.AccessDenied",
                "Customers can only comment on their own tickets."));
        }

        var commentResult = ticket.AddComment(
            command.Body,
            command.Type,
            command.AuthorUserId,
            command.IsCustomer);

        if (commentResult.IsFailure)
        {
            return Result.Failure<TicketCommentDto>(commentResult.Error);
        }

        try
        {
            await _ticketingDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<TicketCommentDto>(Error.Conflict(
                "Ticket.ConcurrencyConflict",
                "The ticket was modified by another request. Please refresh and retry."));
        }

        return Result.Success(commentResult.Value.ToDto());
    }
}
