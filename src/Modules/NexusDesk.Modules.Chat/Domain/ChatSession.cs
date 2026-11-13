using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Chat.Domain;

public sealed class ChatSession : AggregateRoot<Guid>, ITenantScoped
{
    private readonly List<ChatMessage> _messages = [];

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public string Subject { get; private set; }
    public ChatSessionStatus Status { get; private set; }
    public Guid? ConvertedTicketId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    private ChatSession(
        Guid id,
        Guid tenantId,
        Guid customerId,
        string subject,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        Subject = subject;
        Status = ChatSessionStatus.Waiting;
        CreatedAtUtc = createdAtUtc;
    }

    public static Result<ChatSession> Create(
        Guid tenantId,
        Guid customerId,
        string subject,
        DateTimeOffset? utcNow = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<ChatSession>(Error.Validation("ChatSession.InvalidTenant", "TenantId is required."));
        }

        if (customerId == Guid.Empty)
        {
            return Result.Failure<ChatSession>(Error.Validation("ChatSession.InvalidCustomer", "CustomerId is required."));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Result.Failure<ChatSession>(Error.Validation("ChatSession.EmptySubject", "Chat session subject is required."));
        }

        var session = new ChatSession(
            Guid.NewGuid(),
            tenantId,
            customerId,
            subject.Trim(),
            utcNow ?? DateTimeOffset.UtcNow);

        return Result.Success(session);
    }

    public Result Claim(Guid agentId)
    {
        if (agentId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("ChatSession.InvalidAgent", "AgentId is required to claim session."));
        }

        if (Status == ChatSessionStatus.Closed || Status == ChatSessionStatus.ConvertedToTicket)
        {
            return Result.Failure(Error.Conflict(
                "ChatSession.AlreadyClosed",
                "Cannot claim a closed or converted chat session."));
        }

        if (Status != ChatSessionStatus.Waiting)
        {
            return Result.Failure(Error.Conflict(
                "ChatSession.NotWaiting",
                $"Session cannot be claimed because current status is '{Status}'."));
        }

        AssignedAgentId = agentId;
        Status = ChatSessionStatus.Active;
        return Result.Success();
    }

    public Result<ChatMessage> AddMessage(
        Guid senderUserId,
        string senderRole,
        string content,
        DateTimeOffset? utcNow = null)
    {
        if (Status == ChatSessionStatus.Closed || Status == ChatSessionStatus.ConvertedToTicket)
        {
            return Result.Failure<ChatMessage>(Error.Conflict(
                "ChatSession.AlreadyClosed",
                "Cannot append messages to a closed or converted chat session."));
        }

        var messageResult = ChatMessage.Create(
            TenantId,
            Id,
            senderUserId,
            senderRole,
            content,
            utcNow);

        if (messageResult.IsFailure)
        {
            return messageResult;
        }

        _messages.Add(messageResult.Value);
        return messageResult;
    }

    public Result Close(DateTimeOffset? utcNow = null)
    {
        if (Status == ChatSessionStatus.Closed || Status == ChatSessionStatus.ConvertedToTicket)
        {
            return Result.Failure(Error.Conflict(
                "ChatSession.AlreadyClosed",
                "The chat session is already closed or converted."));
        }

        Status = ChatSessionStatus.Closed;
        ClosedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    public Result ConvertToTicket(Guid ticketId, DateTimeOffset? utcNow = null)
    {
        if (ticketId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("ChatSession.InvalidTicket", "ConvertedTicketId is required."));
        }

        if (Status == ChatSessionStatus.Closed || Status == ChatSessionStatus.ConvertedToTicket)
        {
            return Result.Failure(Error.Conflict(
                "ChatSession.AlreadyClosed",
                "The chat session is already closed or converted."));
        }

        Status = ChatSessionStatus.ConvertedToTicket;
        ConvertedTicketId = ticketId;
        ClosedAtUtc = utcNow ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    private ChatSession() : base()
    {
        Subject = string.Empty;
    }
}
