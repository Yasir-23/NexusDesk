namespace NexusDesk.SharedKernel.MultiTenancy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class AllowCrossTenantQueryAttribute : Attribute
{
    public string Reason { get; set; } = string.Empty;

    public AllowCrossTenantQueryAttribute()
    {
    }

    public AllowCrossTenantQueryAttribute(string reason)
    {
        Reason = reason;
    }
}
