namespace NexusDesk.SharedKernel.MultiTenancy.Exceptions;

public class TenantNotFoundException : Exception
{
    public string TenantIdentifier { get; }

    public TenantNotFoundException(string tenantIdentifier)
        : base($"Tenant '{tenantIdentifier}' could not be resolved or was not found.")
    {
        TenantIdentifier = tenantIdentifier;
    }
}
