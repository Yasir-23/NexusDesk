using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Analytics.Domain;

public sealed class UserNotification : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? LinkUrl { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private UserNotification() : base(Guid.Empty)
    {
    }

    public UserNotification(
        Guid id,
        Guid tenantId,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? linkUrl = null,
        bool isRead = false,
        DateTimeOffset? readAtUtc = null,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        TenantId = tenantId;
        RecipientUserId = recipientUserId;
        Type = type ?? string.Empty;
        Title = title ?? string.Empty;
        Message = message ?? string.Empty;
        LinkUrl = linkUrl;
        IsRead = isRead;
        ReadAtUtc = readAtUtc;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }

    public static UserNotification Create(
        Guid tenantId,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? linkUrl = null)
    {
        return new UserNotification(
            Guid.NewGuid(),
            tenantId,
            recipientUserId,
            type,
            title,
            message,
            linkUrl,
            isRead: false,
            readAtUtc: null,
            createdAtUtc: DateTimeOffset.UtcNow);
    }

    public void MarkAsRead(DateTimeOffset? readAtUtc = null)
    {
        if (!IsRead)
        {
            IsRead = true;
            ReadAtUtc = readAtUtc ?? DateTimeOffset.UtcNow;
        }
    }
}
