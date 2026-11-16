using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Analytics.Domain;

public sealed class AuditLogEntry : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string? ActorEmail { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string? MetadataJson { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private AuditLogEntry() : base(Guid.Empty)
    {
    }

    public AuditLogEntry(
        Guid id,
        Guid tenantId,
        string action,
        string resourceType,
        string resourceId,
        Guid? actorUserId = null,
        string? actorEmail = null,
        string? metadataJson = null,
        string? ipAddress = null,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        TenantId = tenantId;
        Action = action ?? string.Empty;
        ResourceType = resourceType ?? string.Empty;
        ResourceId = resourceId ?? string.Empty;
        ActorUserId = actorUserId;
        ActorEmail = actorEmail;
        MetadataJson = metadataJson;
        IpAddress = ipAddress;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
    }
}
