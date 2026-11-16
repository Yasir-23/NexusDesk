using NexusDesk.Modules.Analytics.Domain;
using NexusDesk.Modules.Analytics.Persistence;
using NexusDesk.SharedKernel.Audit;

namespace NexusDesk.Modules.Analytics.Services;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly AnalyticsDbContext _dbContext;

    public AuditLogWriter(AnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(
        Guid tenantId,
        string action,
        string resourceType,
        string resourceId,
        Guid? actorUserId = null,
        string? actorEmail = null,
        string? metadataJson = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry(
            Guid.NewGuid(),
            tenantId,
            action,
            resourceType,
            resourceId,
            actorUserId,
            actorEmail,
            metadataJson,
            ipAddress,
            DateTimeOffset.UtcNow);

        _dbContext.AuditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
