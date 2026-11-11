using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Domain;

public sealed class TenantAuditLog : Entity<Guid>
{
    public Guid TenantId { get; private set; }
    public string Action { get; private set; }
    public string Reason { get; private set; }
    public string PerformedBy { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }

    public TenantAuditLog(
        Guid id,
        Guid tenantId,
        string action,
        string reason,
        string performedBy,
        DateTimeOffset timestampUtc) : base(id)
    {
        TenantId = tenantId;
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Reason = reason ?? string.Empty;
        PerformedBy = performedBy ?? "System";
        TimestampUtc = timestampUtc;
    }

    private TenantAuditLog() : base()
    {
        Action = string.Empty;
        Reason = string.Empty;
        PerformedBy = string.Empty;
    }
}
