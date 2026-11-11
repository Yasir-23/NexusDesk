namespace NexusDesk.SharedKernel.MultiTenancy.Exceptions;

public class TenantSuspendedException : Exception
{
    public string TenantIdentifier { get; }

    public TenantSuspendedException(string tenantIdentifier)
        : base($"Tenant '{tenantIdentifier}' is suspended.")
    {
        TenantIdentifier = tenantIdentifier;
    }
}
