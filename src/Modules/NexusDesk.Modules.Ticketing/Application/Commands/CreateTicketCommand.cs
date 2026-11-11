using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Persistence;
using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Commands;

public sealed record CreateTicketCommand(
    string Title,
    string Description,
    TicketPriority Priority,
    TicketChannel Channel,
    Guid? CustomerId,
    Guid CallerUserId,
    bool IsCustomerCaller,
    IReadOnlyList<string>? Tags);

public sealed class CreateTicketCommandHandler
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly IdentityDbContext _identityDb;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly ITicketNumberGenerator _ticketNumberGenerator;

    public CreateTicketCommandHandler(
        TicketingDbContext ticketingDb,
        IdentityDbContext identityDb,
        ITenantContextAccessor tenantContextAccessor,
        ITicketNumberGenerator ticketNumberGenerator)
    {
        _ticketingDb = ticketingDb;
        _identityDb = identityDb;
        _tenantContextAccessor = tenantContextAccessor;
        _ticketNumberGenerator = ticketNumberGenerator;
    }

    public async Task<Result<TicketDetailsResponse>> HandleAsync(
        CreateTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantContext = _tenantContextAccessor.TenantContext;
        if (tenantContext == null || !tenantContext.IsResolved || !tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Validation(
                "Ticket.TenantNotResolved",
                "A valid active tenant is required to create a ticket."));
        }

        var tenantId = tenantContext.TenantId.Value;

        if (command.CallerUserId == Guid.Empty)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Unauthorized(
                "Ticket.InvalidCaller",
                "Authenticated user identity is required."));
        }

        // Customers always create tickets for themselves; agents/admins may specify a CustomerId
        var effectiveCustomerId = command.IsCustomerCaller
            ? command.CallerUserId
            : (command.CustomerId.HasValue && command.CustomerId.Value != Guid.Empty
                ? command.CustomerId.Value
                : command.CallerUserId);

        if (command.IsCustomerCaller && command.CustomerId.HasValue && command.CustomerId.Value != Guid.Empty && command.CustomerId.Value != command.CallerUserId)
        {
            return Result.Failure<TicketDetailsResponse>(Error.Forbidden(
                "Ticket.CannotCreateForAnotherCustomer",
                "Customers cannot create tickets on behalf of other users."));
        }

        // If an explicit CustomerId other than CallerUserId was provided, verify the customer belongs to the same tenant
        if (effectiveCustomerId != command.CallerUserId)
        {
            var customerExistsInTenant = await _identityDb.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == effectiveCustomerId && u.TenantId == tenantId && u.IsActive, cancellationToken);

            if (!customerExistsInTenant)
            {
                return Result.Failure<TicketDetailsResponse>(Error.NotFound(
                    "Ticket.CustomerNotFound",
                    $"Customer '{effectiveCustomerId}' was not found in the current tenant."));
            }
        }

        // Pre-validate ticket invariants before allocating a sequential ticket number
        var preCheck = Ticket.Create(
            tenantId,
            "TICK-PRECHECK",
            command.Title,
            command.Description,
            command.Priority,
            command.Channel,
            effectiveCustomerId,
            command.Tags);

        if (preCheck.IsFailure)
        {
            return Result.Failure<TicketDetailsResponse>(preCheck.Error);
        }

        var strategy = _ticketingDb.Database.CreateExecutionStrategy();
        var createdTicket = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _ticketingDb.Database.BeginTransactionAsync(cancellationToken);

            var ticketNumber = await _ticketNumberGenerator.NextTicketNumberAsync(tenantId, cancellationToken);
            var createResult = Ticket.Create(
                tenantId,
                ticketNumber,
                command.Title,
                command.Description,
                command.Priority,
                command.Channel,
                effectiveCustomerId,
                command.Tags);

            if (createResult.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return createResult;
            }

            _ticketingDb.Tickets.Add(createResult.Value);
            await _ticketingDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return createResult;
        });

        if (createdTicket.IsFailure)
        {
            return Result.Failure<TicketDetailsResponse>(createdTicket.Error);
        }

        return Result.Success(createdTicket.Value.ToDetailsResponse(command.IsCustomerCaller));
    }
}
