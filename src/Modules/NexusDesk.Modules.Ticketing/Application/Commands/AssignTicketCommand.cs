using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Persistence;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Commands;

public sealed record AssignTicketCommand(
    Guid TicketId,
    Guid AgentId,
    Guid PerformedByUserId,
    uint? RowVersion = null);

public sealed class AssignTicketCommandHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly IdentityDbContext _identityDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public AssignTicketCommandHandler(
        TicketingDbContext ticketingDb,
        IdentityDbContext identityDb,
        ITenantContextAccessor tenantContextAccessor)
    {
        _ticketingDb = ticketingDb;
        _identityDb = identityDb;
        _tenantContextAccessor = tenantContextAccessor;
    }

    public async Task<Result<TicketDetailsResponse>> HandleAsync(
        AssignTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.TenantNotResolved",
                "A valid active tenant is required."));
        }

        if (command.AgentId == Guid.Empty)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.InvalidAgent",
                "Agent ID cannot be empty."));
        }

        // Verify agent exists in the same tenant and has Agent or TenantAdmin role
        var agent = await _identityDb.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == command.AgentId && u.TenantId == tenantId && u.IsActive, cancellationToken);

        if (agent == null)
        {
            return Result.Failure<TicketDetailsResponse>(Error.NotFound(
                "Ticket.AgentNotFound",
                $"Agent '{command.AgentId}' was not found in the current tenant."));
        }

        if (!string.Equals(agent.Role, IdentityRoles.Agent, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(agent.Role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.InvalidAgentRole",
                "Tickets can only be assigned to an active Agent or TenantAdmin."));
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

        var result = ticket.AssignToAgent(command.AgentId, command.PerformedByUserId);
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
