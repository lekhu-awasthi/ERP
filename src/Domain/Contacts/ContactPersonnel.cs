namespace ErpApp.Domain.Contacts;

/// <summary>
/// "Contact Personnel" sub-contacts (product-requirements.md FR-4.5). Modeled as a standalone
/// entity referencing ContactId directly -- like WorkTask/Deal reference their parent -- not as an
/// encapsulated child collection loaded and wholesale-replaced on Contact.Update the way
/// Invoice/PurchaseBill lines are. docs/phase-18-status.md decision #4: the live Tigg "Contact
/// Personnel" tab adds/edits/removes exactly one row at a time via its own dialog (never a bulk
/// list submit), so each write here is its own command (Add/Update/Remove) issuing its own
/// SaveChanges directly against ContactPersonnel's own DbSet -- CLAUDE.md's Phase 4 full-collection-
/// replace gotcha (Clear()+re-Add mistracked by the InMemory EF provider) doesn't apply because
/// there is no full-collection replace anywhere in this design, by construction, not by mitigation.
///
/// Field shape confirmed live against the Tigg reference product's own "Add Contact Personnel"
/// dialog: Name (required), Address, Code, Phone Number, Group (ContactGroup), Email, Organization
/// Title (a free-text role/designation, e.g. "Manager") -- the dialog's own "Select Organization"
/// field is just the parent Contact itself (read-only in context, since Personnel is always added
/// from within one Contact's own detail page), so it's not modeled as a separate field here.
/// </summary>
public sealed class ContactPersonnel
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Address { get; private set; }
    public string? Code { get; private set; }
    public string? Phone { get; private set; }
    public Guid? GroupId { get; private set; }
    public string? Email { get; private set; }
    public string? OrganizationTitle { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ContactPersonnel()
    {
    }

    public static ContactPersonnel Create(
        Guid organizationId,
        Guid contactId,
        string name,
        string? address,
        string? code,
        string? phone,
        Guid? groupId,
        string? email,
        string? organizationTitle)
    {
        return new ContactPersonnel
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Name = name,
            Address = address,
            Code = code,
            Phone = phone,
            GroupId = groupId,
            Email = email,
            OrganizationTitle = organizationTitle,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string name,
        string? address,
        string? code,
        string? phone,
        Guid? groupId,
        string? email,
        string? organizationTitle)
    {
        Name = name;
        Address = address;
        Code = code;
        Phone = phone;
        GroupId = groupId;
        Email = email;
        OrganizationTitle = organizationTitle;
    }
}
