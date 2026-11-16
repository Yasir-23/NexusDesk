namespace NexusDesk.SharedKernel.Audit;

public interface IAuditLogWriter
{
    Task WriteAsync(
        Guid tenantId,
        string action,
        string resourceType,
        string resourceId,
        Guid? actorUserId = null,
        string? actorEmail = null,
        string? metadataJson = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
