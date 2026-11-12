using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.Modules.Ticketing.Domain.Events;
using NexusDesk.Modules.Ticketing.Domain.ValueObjects;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain.Entities;

public sealed class Ticket : AggregateRoot<Guid>, ITenantScoped
{
    public const int MaxTitleLength = 200;

    private readonly List<TicketComment> _comments = [];
    private readonly List<TicketTag> _tags = [];
    private readonly List<TicketActivityLog> _activityLogs = [];

    public Guid TenantId { get; private set; }
    public string TicketNumber { get; private set; }
    public string Title { get; private set; }
    public string Description { get; private set; }
    public TicketStatus Status { get; private set; }
    public TicketPriority Priority { get; private set; }
    public TicketChannel Channel { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTimeOffset? FirstResponseDueUtc { get; private set; }
    public DateTimeOffset? ResolutionDueUtc { get; private set; }
    public DateTimeOffset? FirstRespondedAtUtc { get; private set; }
    public bool IsSlaBreached { get; private set; }
    public DateTimeOffset? SlaBreachedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<TicketComment> Comments => _comments.AsReadOnly();
    public IReadOnlyCollection<TicketTag> Tags => _tags.AsReadOnly();
    public IReadOnlyCollection<TicketActivityLog> ActivityLogs => _activityLogs.AsReadOnly();

    private Ticket(
        Guid id,
        Guid tenantId,
        string ticketNumber,
        string title,
        string description,
        TicketPriority priority,
        TicketChannel channel,
        Guid customerId,
        SlaTargets slaTargets,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        TicketNumber = ticketNumber;
        Title = title;
        Description = description;
        Status = TicketStatus.New;
        Priority = priority;
        Channel = channel;
        CustomerId = customerId;
        FirstResponseDueUtc = slaTargets.FirstResponseDueUtc;
        ResolutionDueUtc = slaTargets.ResolutionDueUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    private Ticket() : base()
    {
        TicketNumber = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
    }

    /// <summary>
    /// Creates a new ticket aggregate with calculated SLA targets, initial activity log, and domain event.
    /// </summary>
    public static Result<Ticket> Create(
        Guid tenantId,
        string ticketNumber,
        string title,
        string description,
        TicketPriority priority,
        TicketChannel channel,
        Guid customerId,
        IEnumerable<string>? tags = null,
        DateTimeOffset? utcNow = null,
        SlaTargets? customSlaTargets = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.InvalidTenant", "Tenant ID is required."));
        }

        if (string.IsNullOrWhiteSpace(ticketNumber))
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.InvalidNumber", "Ticket number is required."));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.TitleRequired", "Ticket title is required."));
        }

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTitleLength)
        {
            return Result.Failure<Ticket>(Error.Validation(
                "Ticket.TitleTooLong",
                $"Ticket title cannot exceed {MaxTitleLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.DescriptionRequired", "Ticket description is required."));
        }

        if (!Enum.IsDefined(priority))
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.InvalidPriority", "Ticket priority is invalid."));
        }

        if (!Enum.IsDefined(channel))
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.InvalidChannel", "Ticket channel is invalid."));
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure<Ticket>(Error.Validation("Ticket.InvalidCustomer", "Customer ID is required."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var slaTargets = customSlaTargets ?? SlaPolicyMatrix.CalculateTargets(priority, now);
        var ticketId = Guid.NewGuid();
        var normalizedNumber = ticketNumber.Trim();

        var ticket = new Ticket(
            ticketId,
            tenantId,
            normalizedNumber,
            trimmedTitle,
            description.Trim(),
            priority,
            channel,
            customerId,
            slaTargets,
            now);

        if (tags != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawTag in tags)
            {
                if (string.IsNullOrWhiteSpace(rawTag))
                {
                    continue;
                }

                var slug = TicketTag.NormalizeSlug(rawTag);
                if (seen.Add(slug))
                {
                    ticket._tags.Add(new TicketTag(Guid.NewGuid(), tenantId, ticketId, slug));
                }
            }
        }

        ticket.RecordActivity(customerId, "Created", null, normalizedNumber, now);
        ticket.AddDomainEvent(new TicketCreatedDomainEvent(ticketId, tenantId, normalizedNumber, title, priority.ToString(), customerId, now));

        return Result.Success(ticket);
    }

    /// <summary>
    /// Assigns the ticket to an agent and transitions status from New to Open.
    /// </summary>
    public Result AssignToAgent(Guid agentId, Guid performedByUserId, DateTimeOffset? utcNow = null)
    {
        if (agentId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidAgent", "Assigned agent ID cannot be empty."));
        }

        if (performedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Performing user ID cannot be empty."));
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.ClosedCannotAssign",
                "Cannot assign a closed ticket. Reopen the ticket first."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var previousAgentId = AssignedAgentId;
        AssignedAgentId = agentId;

        RecordActivity(performedByUserId, "Assigned", previousAgentId?.ToString(), agentId.ToString(), now);

        if (Status == TicketStatus.New)
        {
            TransitionStatusInternal(TicketStatus.Open, performedByUserId, now);
        }

        UpdatedAtUtc = now;
        AddDomainEvent(new TicketAssignedDomainEvent(Id, agentId, performedByUserId));

        return Result.Success();
    }

    /// <summary>
    /// Changes ticket priority and recalculates SLA targets if they have not yet been breached.
    /// </summary>
    public Result ChangePriority(
        TicketPriority newPriority,
        Guid performedByUserId,
        DateTimeOffset? utcNow = null,
        SlaTargets? customSlaTargets = null)
    {
        if (!Enum.IsDefined(newPriority))
        {
            return Result.Failure(Error.Validation("Ticket.InvalidPriority", "Ticket priority is invalid."));
        }

        if (performedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Performing user ID cannot be empty."));
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.ClosedCannotModify",
                "Cannot change priority on a closed ticket."));
        }

        if (Priority == newPriority)
        {
            return Result.Success();
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var oldPriority = Priority;
        Priority = newPriority;

        var recalculatedTargets = customSlaTargets ?? SlaPolicyMatrix.CalculateTargets(newPriority, CreatedAtUtc);

        if (FirstRespondedAtUtc == null && (!FirstResponseDueUtc.HasValue || now <= FirstResponseDueUtc.Value))
        {
            FirstResponseDueUtc = recalculatedTargets.FirstResponseDueUtc;
        }

        if (ResolvedAtUtc == null && (!ResolutionDueUtc.HasValue || now <= ResolutionDueUtc.Value))
        {
            ResolutionDueUtc = recalculatedTargets.ResolutionDueUtc;
        }

        RecordActivity(performedByUserId, "PriorityChanged", oldPriority.ToString(), newPriority.ToString(), now);
        UpdatedAtUtc = now;
        AddDomainEvent(new TicketPriorityChangedDomainEvent(
            Id,
            TenantId,
            TicketNumber,
            oldPriority,
            newPriority,
            performedByUserId));

        return Result.Success();
    }

    /// <summary>
    /// Appends a public reply or internal note, handling SLA first-response tracking and customer reopen rules.
    /// </summary>
    public Result<TicketComment> AddComment(
        string body,
        CommentType type,
        Guid authorUserId,
        bool isCustomer,
        DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure<TicketComment>(Error.Validation("Ticket.EmptyComment", "Comment body cannot be empty."));
        }

        if (authorUserId == Guid.Empty)
        {
            return Result.Failure<TicketComment>(Error.Validation("Ticket.InvalidActor", "Author user ID cannot be empty."));
        }

        if (!Enum.IsDefined(type))
        {
            return Result.Failure<TicketComment>(Error.Validation("Ticket.InvalidCommentType", "Comment type is invalid."));
        }

        if (isCustomer && type == CommentType.InternalNote)
        {
            return Result.Failure<TicketComment>(Error.Forbidden(
                "Ticket.CustomerInternalNoteForbidden",
                "Customers are not permitted to add internal notes."));
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure<TicketComment>(Error.Validation(
                "Ticket.ClosedCannotComment",
                "Cannot add comments to a closed ticket. Reopen the ticket first."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        var comment = new TicketComment(
            Guid.NewGuid(),
            TenantId,
            Id,
            authorUserId,
            isCustomer,
            type,
            body.Trim(),
            now);

        _comments.Add(comment);

        if (isCustomer && Status == TicketStatus.PendingCustomer)
        {
            TransitionStatusInternal(TicketStatus.Open, authorUserId, now);
        }

        if (!isCustomer && type == CommentType.PublicReply && FirstRespondedAtUtc == null)
        {
            FirstRespondedAtUtc = now;
        }

        RecordActivity(authorUserId, "CommentAdded", null, type.ToString(), now);
        UpdatedAtUtc = now;
        AddDomainEvent(new TicketCommentAddedDomainEvent(Id, comment.Id, type, isCustomer));

        return Result.Success(comment);
    }

    /// <summary>
    /// Transitions an active ticket to Open. Resolved or Closed tickets must use Reopen().
    /// </summary>
    public Result MoveToOpen(Guid performedByUserId, DateTimeOffset? utcNow = null)
    {
        if (performedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Performing user ID cannot be empty."));
        }

        if (Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.ReopenRequired",
                "Resolved or closed tickets must be reopened with a reason."));
        }

        if (Status == TicketStatus.Open)
        {
            return Result.Success();
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        TransitionStatusInternal(TicketStatus.Open, performedByUserId, now);
        UpdatedAtUtc = now;

        return Result.Success();
    }

    /// <summary>
    /// Transitions an active ticket to PendingCustomer.
    /// </summary>
    public Result MoveToPendingCustomer(Guid performedByUserId, DateTimeOffset? utcNow = null)
        => TransitionToPending(TicketStatus.PendingCustomer, performedByUserId, utcNow);

    /// <summary>
    /// Transitions an active ticket to PendingInternal.
    /// </summary>
    public Result MoveToPendingInternal(Guid performedByUserId, DateTimeOffset? utcNow = null)
        => TransitionToPending(TicketStatus.PendingInternal, performedByUserId, utcNow);

    /// <summary>
    /// Resolves the ticket with mandatory resolution notes.
    /// </summary>
    public Result Resolve(string resolutionNotes, Guid resolvedByUserId, DateTimeOffset? utcNow = null)
    {
        if (resolvedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Resolving user ID cannot be empty."));
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.CannotResolveClosed",
                "Cannot resolve a closed ticket without reopening it first."));
        }

        if (Status == TicketStatus.Resolved)
        {
            return Result.Failure(Error.Validation(
                "Ticket.AlreadyResolved",
                "Ticket is already resolved."));
        }

        if (string.IsNullOrWhiteSpace(resolutionNotes))
        {
            return Result.Failure(Error.Validation(
                "Ticket.ResolutionNotesRequired",
                "Resolution notes are required when resolving a ticket."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        ResolutionNotes = resolutionNotes.Trim();
        ResolvedAtUtc = now;
        TransitionStatusInternal(TicketStatus.Resolved, resolvedByUserId, now);
        AddDomainEvent(new TicketResolvedDomainEvent(Id, TenantId, TicketNumber, resolvedByUserId, ResolutionNotes, now));
        UpdatedAtUtc = now;

        return Result.Success();
    }

    /// <summary>
    /// Closes the ticket.
    /// </summary>
    public Result Close(Guid closedByUserId, DateTimeOffset? utcNow = null)
    {
        if (closedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Closing user ID cannot be empty."));
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.AlreadyClosed",
                "Ticket is already closed."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        ClosedAtUtc = now;
        TransitionStatusInternal(TicketStatus.Closed, closedByUserId, now);
        UpdatedAtUtc = now;

        return Result.Success();
    }

    /// <summary>
    /// Reopens a Resolved or Closed ticket back to Open with a mandatory reason.
    /// </summary>
    public Result Reopen(string reason, Guid reopenedByUserId, DateTimeOffset? utcNow = null)
    {
        if (reopenedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Reopening user ID cannot be empty."));
        }

        if (Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            return Result.Failure(Error.Validation(
                "Ticket.InvalidReopen",
                "Only resolved or closed tickets can be reopened."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "Ticket.ReopenReasonRequired",
                "A reason is required to reopen a ticket."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        ResolvedAtUtc = null;
        ClosedAtUtc = null;
        TransitionStatusInternal(TicketStatus.Open, reopenedByUserId, now);
        RecordActivity(reopenedByUserId, "Reopened", null, reason.Trim(), now);
        UpdatedAtUtc = now;

        return Result.Success();
    }

    /// <summary>
    /// Marks the ticket as SLA breached and raises TicketSlaBreachedDomainEvent.
    /// </summary>
    public Result MarkSlaBreached(string breachType, DateTimeOffset breachedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(breachType))
        {
            return Result.Failure(Error.Validation("Ticket.InvalidBreachType", "Breach type is required."));
        }

        if (Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.ClosedOrResolvedCannotBreach",
                "Cannot mark a resolved or closed ticket as SLA breached."));
        }

        if (IsSlaBreached)
        {
            return Result.Success();
        }

        var normalizedBreachType = breachType.Trim();
        IsSlaBreached = true;
        SlaBreachedAtUtc = breachedAtUtc;
        UpdatedAtUtc = breachedAtUtc;

        RecordActivity(Guid.Empty, "SlaBreached", null, normalizedBreachType, breachedAtUtc);
        AddDomainEvent(new TicketSlaBreachedDomainEvent(
            Id,
            TenantId,
            TicketNumber,
            Priority.ToString(),
            normalizedBreachType,
            breachedAtUtc));

        return Result.Success();
    }

    /// <summary>
    /// Returns comments visible to the caller based on role (customers cannot see internal notes).
    /// </summary>
    public IReadOnlyList<TicketComment> GetVisibleComments(bool isCustomer)
    {
        var query = isCustomer
            ? _comments.Where(c => c.Type == CommentType.PublicReply)
            : _comments;

        return query.OrderBy(c => c.CreatedAtUtc).ToList().AsReadOnly();
    }

    private Result TransitionToPending(TicketStatus targetStatus, Guid performedByUserId, DateTimeOffset? utcNow)
    {
        if (performedByUserId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("Ticket.InvalidActor", "Performing user ID cannot be empty."));
        }

        if (Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return Result.Failure(Error.Validation(
                "Ticket.InvalidTransition",
                $"Cannot transition ticket from '{Status}' to '{targetStatus}' without reopening first."));
        }

        if (Status == targetStatus)
        {
            return Result.Success();
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        TransitionStatusInternal(targetStatus, performedByUserId, now);
        UpdatedAtUtc = now;

        return Result.Success();
    }

    private void TransitionStatusInternal(TicketStatus newStatus, Guid performedByUserId, DateTimeOffset timestampUtc)
    {
        var oldStatus = Status;
        Status = newStatus;
        RecordActivity(performedByUserId, "StatusChanged", oldStatus.ToString(), newStatus.ToString(), timestampUtc);
        AddDomainEvent(new TicketStatusChangedDomainEvent(Id, oldStatus, newStatus, performedByUserId));
    }

    private void RecordActivity(
        Guid performedByUserId,
        string action,
        string? oldValue,
        string? newValue,
        DateTimeOffset timestampUtc)
    {
        _activityLogs.Add(new TicketActivityLog(
            Guid.NewGuid(),
            TenantId,
            Id,
            performedByUserId,
            action,
            oldValue,
            newValue,
            timestampUtc));
    }
}
