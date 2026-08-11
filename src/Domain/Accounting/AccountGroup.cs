using ErpApp.Domain.Common;

namespace ErpApp.Domain.Accounting;

/// <summary>
/// Self-referencing tree of the Chart of Accounts (architecture-spec.md §4.7), adjacency-list
/// pattern per architecture-spec.md §5 (ParentGroupId self-FK), same shape as Contacts.ContactGroup/
/// Catalog.ProductCategory. Implements ITenantLookupEntity so it reuses the generic
/// ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; handlers.
///
/// RootType is set once at Create and never changed by Update -- regrouping an Asset group into
/// Liability is the same modeling smell Phase 3 ruled out for Contact.Type/Product.Type.
/// </summary>
public sealed class AccountGroup : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public AccountRootType RootType { get; private set; }
    public Guid? ParentGroupId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AccountGroup()
    {
    }

    public static AccountGroup Create(Guid organizationId, string name, AccountRootType rootType, Guid? parentGroupId)
    {
        return new AccountGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            RootType = rootType,
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
