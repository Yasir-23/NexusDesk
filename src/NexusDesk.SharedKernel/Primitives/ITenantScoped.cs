namespace NexusDesk.SharedKernel.Primitives;

public interface ITenantScoped
{
    Guid TenantId { get; }
}
