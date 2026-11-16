namespace NexusDesk.SharedKernel.Notifications;

public interface INotificationService
{
    Task<Guid> CreateNotificationAsync(
        Guid tenantId,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? linkUrl = null,
        CancellationToken cancellationToken = default);
}
