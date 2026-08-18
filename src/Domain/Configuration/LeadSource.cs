using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// CRM (config) > Lead Source (erp-module-scan.md line 311-312): {id, name}. Modeled as a real
/// tenant-editable lookup entity reusing the generic ListLookupsQuery&lt;TLookup&gt;/
/// DeleteLookupCommand&lt;TLookup&gt; pair (Application.Configuration), the same "confirmed
/// dedicated management screen -> generic lookup entity" precedent TaskType established in Phase
/// 13 -- see Deal's own doc comment for the fuller reasoning.
/// </summary>
public sealed class LeadSource : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private LeadSource()
    {
    }

    public static LeadSource Create(Guid organizationId, string name)
    {
        return new LeadSource
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isActive)
    {
        Name = name;
        IsActive = isActive;
    }
}
