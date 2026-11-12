using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.Modules.Ticketing.Domain.ValueObjects;
using NexusDesk.Modules.Ticketing.Infrastructure.Persistence;
using NexusDesk.SharedKernel.Automation;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Application.Automation;

public sealed class TicketingAutomationEventBridge :
    IDomainEventHandler<TicketCreatedDomainEvent>,
    IDomainEventHandler<TicketPriorityChangedDomainEvent>,
    IDomainEventHandler<TicketSlaBreachedDomainEvent>
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly IAutomationRuleEngine? _ruleEngine;

    public TicketingAutomationEventBridge(
        TicketingDbContext ticketingDb,
        IAutomationRuleEngine? ruleEngine = null)
    {
        _ticketingDb = ticketingDb;
        _ruleEngine = ruleEngine;
    }

    public async Task HandleAsync(TicketCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_ruleEngine == null)
        {
            return;
        }

        var ticket = await _ticketingDb.Tickets
            .AsNoTracking()
            .Where(t => t.Id == domainEvent.TicketId && t.TenantId == domainEvent.TenantId)
            .Select(t => new { t.Priority, t.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket == null)
        {
            return;
        }

        await _ruleEngine.EvaluateAndExecuteAsync(
            new TicketAutomationEventContext(
                domainEvent.TicketId,
                domainEvent.TenantId,
                domainEvent.TicketNumber,
                "TicketCreated",
                ticket.Priority.ToString(),
                PerformedByUserId: ticket.CustomerId,
                OccurredAtUtc: domainEvent.OccurredOnUtc),
            cancellationToken);
    }

    public async Task HandleAsync(TicketPriorityChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_ruleEngine == null)
        {
            return;
        }

        await _ruleEngine.EvaluateAndExecuteAsync(
            new TicketAutomationEventContext(
                domainEvent.TicketId,
                domainEvent.TenantId,
                domainEvent.TicketNumber,
                "TicketPriorityChanged",
                domainEvent.NewPriority.ToString(),
                OldPriority: domainEvent.OldPriority.ToString(),
                PerformedByUserId: domainEvent.PerformedByUserId,
                OccurredAtUtc: domainEvent.OccurredOnUtc),
            cancellationToken);
    }

    public async Task HandleAsync(TicketSlaBreachedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_ruleEngine == null)
        {
            return;
        }

        await _ruleEngine.EvaluateAndExecuteAsync(
            new TicketAutomationEventContext(
                domainEvent.TicketId,
                domainEvent.TenantId,
                domainEvent.TicketNumber,
                "TicketSlaBreached",
                domainEvent.Priority,
                BreachType: domainEvent.BreachType,
                OccurredAtUtc: domainEvent.BreachedAtUtc),
            cancellationToken);
    }
}

public sealed class TicketAutomationActions : ITicketAutomationActions
{
    private readonly TicketingDbContext _ticketingDb;
    private readonly ISlaPolicyProvider? _slaPolicyProvider;

    public TicketAutomationActions(
        TicketingDbContext ticketingDb,
        ISlaPolicyProvider? slaPolicyProvider = null)
    {
        _ticketingDb = ticketingDb;
        _slaPolicyProvider = slaPolicyProvider;
    }

    public async Task<bool> EscalatePriorityAsync(
        Guid tenantId,
        Guid ticketId,
        string targetPriority,
        Guid performedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<TicketPriority>(targetPriority, true, out var parsedPriority))
        {
            return false;
        }

        var ticket = await _ticketingDb.Tickets
            .Include(t => t.ActivityLogs)
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.TenantId == tenantId, cancellationToken);

        if (ticket == null || ticket.Priority == parsedPriority)
        {
            return false;
        }

        SlaTargets? customTargets = null;
        if (_slaPolicyProvider != null)
        {
            var policy = await _slaPolicyProvider.GetPolicyTargetsAsync(
                tenantId,
                parsedPriority.ToString(),
                cancellationToken);
            if (policy.HasValue)
            {
                customTargets = new SlaTargets(
                    ticket.CreatedAtUtc.AddMinutes(policy.Value.FirstResponseTargetMinutes),
                    ticket.CreatedAtUtc.AddMinutes(policy.Value.ResolutionTargetMinutes));
            }
        }

        var result = ticket.ChangePriority(parsedPriority, performedByUserId, customSlaTargets: customTargets);
        if (result.IsFailure)
        {
            return false;
        }

        await _ticketingDb.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AssignAgentAsync(
        Guid tenantId,
        Guid ticketId,
        Guid agentId,
        Guid performedByUserId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _ticketingDb.Tickets
            .Include(t => t.ActivityLogs)
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.TenantId == tenantId, cancellationToken);

        if (ticket == null)
        {
            return false;
        }

        var result = ticket.AssignToAgent(agentId, performedByUserId);
        if (result.IsFailure)
        {
            return false;
        }

        await _ticketingDb.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddInternalNoteAsync(
        Guid tenantId,
        Guid ticketId,
        string noteBody,
        Guid authorUserId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _ticketingDb.Tickets
            .Include(t => t.Comments)
            .Include(t => t.ActivityLogs)
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.TenantId == tenantId, cancellationToken);

        if (ticket == null)
        {
            return false;
        }

        var result = ticket.AddComment(noteBody, CommentType.InternalNote, authorUserId, isCustomer: false);
        if (result.IsFailure)
        {
            return false;
        }

        await _ticketingDb.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class TicketSlaBreachEvaluator : ITicketSlaBreachEvaluator
{
    private readonly TicketingDbContext _ticketingDb;

    public TicketSlaBreachEvaluator(TicketingDbContext ticketingDb)
    {
        _ticketingDb = ticketingDb;
    }

    public async Task<int> EvaluateTenantBreachesAsync(
        Guid tenantId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _ticketingDb.Tickets
            .Include(t => t.ActivityLogs)
            .Where(t => t.TenantId == tenantId &&
                        !t.IsSlaBreached &&
                        t.Status != TicketStatus.Resolved &&
                        t.Status != TicketStatus.Closed &&
                        ((t.FirstRespondedAtUtc == null && t.FirstResponseDueUtc != null && t.FirstResponseDueUtc < utcNow) ||
                         (t.ResolutionDueUtc != null && t.ResolutionDueUtc < utcNow)))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var breachedCount = 0;
        foreach (var ticket in candidates)
        {
            var isFirstResponseBreach = ticket.FirstRespondedAtUtc == null &&
                                        ticket.FirstResponseDueUtc.HasValue &&
                                        ticket.FirstResponseDueUtc.Value < utcNow;

            var breachType = isFirstResponseBreach ? "FirstResponse" : "Resolution";
            var result = ticket.MarkSlaBreached(breachType, utcNow);
            if (result.IsSuccess)
            {
                breachedCount++;
            }
        }

        if (breachedCount > 0)
        {
            await _ticketingDb.SaveChangesAsync(cancellationToken);
        }

        return breachedCount;
    }
}
