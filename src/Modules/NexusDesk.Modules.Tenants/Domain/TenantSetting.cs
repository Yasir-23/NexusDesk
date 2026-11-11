using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Tenants.Domain;

public sealed class TenantSetting : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Key { get; private set; }
    public string Value { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public TenantSetting(
        Guid id,
        Guid tenantId,
        string key,
        string value,
        DateTimeOffset? createdAtUtc = null) : base(id)
    {
        TenantId = tenantId;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? string.Empty;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        UpdatedAtUtc = null;
    }

    private TenantSetting() : base()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public void UpdateValue(string newValue, DateTimeOffset updatedAtUtc)
    {
        Value = newValue ?? string.Empty;
        UpdatedAtUtc = updatedAtUtc;
    }
}
