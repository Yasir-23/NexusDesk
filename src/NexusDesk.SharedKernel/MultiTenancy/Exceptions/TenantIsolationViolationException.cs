namespace NexusDesk.SharedKernel.MultiTenancy.Exceptions;

public class TenantIsolationViolationException : Exception
{
    public Guid AttemptedTenantId { get; }
    public Guid CurrentTenantId { get; }

    public TenantIsolationViolationException(Guid attemptedTenantId, Guid currentTenantId)
        : base($"Cross-tenant write or modification violation: Attempted '{attemptedTenantId}', current context '{currentTenantId}'.")
    {
        AttemptedTenantId = attemptedTenantId;
        CurrentTenantId = currentTenantId;
    }

    public TenantIsolationViolationException(string message) : base(message)
    {
    }
}
