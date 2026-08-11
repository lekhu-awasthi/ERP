namespace ErpApp.Domain.Accounting;

/// <summary>
/// Leaf of the Chart of Accounts (architecture-spec.md §4.7) -- the target of every "Select
/// Account" picker across the system. Not a plain ITenantLookupEntity, same reasoning as
/// Contacts.Contact/Catalog.Product: it needs an auto-numbered Code, so Create/Update stay
/// concrete.
///
/// Code is assigned at Create (not Approve) via IDocumentNumberGenerator(DocumentType.Account) --
/// Account has no Draft/Approve lifecycle, same "numbering-pool-only code" treatment as
/// Contact.Code/Product.Code.
///
/// RootType is denormalized from the owning AccountGroup at Create (and re-copied on Update if
/// GroupId changes) so every GL line can read an Account's RootType without a join -- the handler
/// is responsible for keeping it in sync with the group's own RootType.
/// </summary>
public sealed class Account
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public AccountRootType RootType { get; private set; }
    public Guid GroupId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Account()
    {
    }

    public static Account Create(Guid organizationId, string code, string name, AccountRootType rootType, Guid groupId)
    {
        return new Account
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = code,
            Name = name,
            RootType = rootType,
            GroupId = groupId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, Guid groupId, AccountRootType rootType, bool isActive)
    {
        Name = name;
        GroupId = groupId;
        RootType = rootType;
        IsActive = isActive;
    }
}
