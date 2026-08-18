using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Workflow (config) > Task Types (erp-module-scan.md line 315): "Follow up, Notify, Email, etc,
/// name+color" -- {id, name, color}. Modeled as a real tenant-editable lookup entity reusing the
/// generic ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; pair
/// (Application.Configuration), not a hardcoded C# enum -- see WorkTask's own remarks on Priority
/// for the competing evidence weighed here: unlike Priority/Status, Type has its own confirmed
/// dedicated management screen in the scan, the same "confirmed lookup screen -> generic lookup
/// entity" precedent every other Configuration lookup (CreditTerm, PaymentMode, TdsType, ...)
/// already follows.
/// </summary>
public sealed class TaskType : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Color { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TaskType()
    {
    }

    public static TaskType Create(Guid organizationId, string name, string color)
    {
        return new TaskType
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Color = color,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, string color, bool isActive)
    {
        Name = name;
        Color = color;
        IsActive = isActive;
    }
}
