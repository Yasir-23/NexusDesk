namespace NexusDesk.SharedKernel.MultiTenancy;

public interface ITenantContextAccessor
{
    ITenantContext TenantContext { get; set; }
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<ITenantContext?> _currentTenantContext = new();

    public ITenantContext TenantContext
    {
        get => _currentTenantContext.Value ?? new TenantContext();
        set => _currentTenantContext.Value = value;
    }
}
