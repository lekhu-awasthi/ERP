using ErpApp.Domain.Common;

namespace ErpApp.Domain.Contacts;

/// <summary>
/// Self-referencing tree for grouping Contacts (architecture-spec.md §4.2), adjacency-list
/// pattern per architecture-spec.md §5 (ParentGroupId self-FK, not HIERARCHYID). Implements
/// ITenantLookupEntity so it can reuse the generic ListLookupsQuery&lt;TLookup&gt;/
/// DeleteLookupCommand&lt;TLookup&gt; handlers (Application.Configuration) -- the extra
/// ParentGroupId doesn't conflict with the interface, same precedent as ReportingTagOption's
/// extra CategoryId.
/// </summary>
public sealed class ContactGroup : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid? ParentGroupId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ContactGroup()
    {
    }

    public static ContactGroup Create(Guid organizationId, string name, Guid? parentGroupId)
    {
        return new ContactGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            ParentGroupId = parentGroupId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, Guid? parentGroupId, bool isActive)
    {
        Name = name;
        ParentGroupId = parentGroupId;
        IsActive = isActive;
    }
}
