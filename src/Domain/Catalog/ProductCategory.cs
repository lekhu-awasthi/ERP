using ErpApp.Domain.Common;

namespace ErpApp.Domain.Catalog;

/// <summary>
/// Self-referencing tree for grouping Products (architecture-spec.md §4.3), same adjacency-list
/// shape as Contacts.ContactGroup (architecture-spec.md §5). Implements ITenantLookupEntity to
/// reuse the generic ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; handlers.
/// </summary>
public sealed class ProductCategory : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid? ParentCategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ProductCategory()
    {
    }

    public static ProductCategory Create(Guid organizationId, string name, Guid? parentCategoryId)
    {
        return new ProductCategory
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            ParentCategoryId = parentCategoryId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, Guid? parentCategoryId, bool isActive)
    {
        Name = name;
        ParentCategoryId = parentCategoryId;
        IsActive = isActive;
    }
}
