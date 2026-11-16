using Microsoft.Extensions.Logging;
using NexusDesk.Modules.Analytics.Domain;
using NexusDesk.Modules.Analytics.Persistence;
using NexusDesk.SharedKernel.Notifications;

namespace NexusDesk.Modules.Analytics.Services;

public sealed class NotificationService : INotificationService
{
    private readonly AnalyticsDbContext _dbContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AnalyticsDbContext dbContext,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Guid> CreateNotificationAsync(
        Guid tenantId,
        Guid recipientUserId,
        string type,
        string title,
        string message,
        string? linkUrl = null,
        CancellationToken cancellationToken = default)
    {
        var notification = UserNotification.Create(
            tenantId,
            recipientUserId,
            type,
            title,
            message,
            linkUrl);

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created in-app notification {NotificationId} of type {Type} for user {UserId} in tenant {TenantId}",
            notification.Id,
            type,
            recipientUserId,
            tenantId);

        return notification.Id;
    }
}
