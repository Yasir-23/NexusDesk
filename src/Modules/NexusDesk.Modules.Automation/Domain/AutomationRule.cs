using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Domain;

public sealed class AutomationRule : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string TriggerEvent { get; private set; }
    public bool IsEnabled { get; private set; }

    public AutomationRule(Guid id, Guid tenantId, string name, string triggerEvent, bool isEnabled = true) : base(id)
    {
        TenantId = tenantId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TriggerEvent = triggerEvent ?? throw new ArgumentNullException(nameof(triggerEvent));
        IsEnabled = isEnabled;
    }

    private AutomationRule() : base()
    {
        Name = string.Empty;
        TriggerEvent = string.Empty;
    }
}
