namespace ErpApp.Domain.Contacts;

/// <summary>
/// Aggregate root unifying Customer/Supplier/Lead (architecture-spec.md §4.2). Not a plain
/// ITenantLookupEntity -- it's not consumed by the generic Configuration lookup handlers, and its
/// field set diverges too much to benefit from that genericity (see phase-2-status.md's "Create/
/// Update stay concrete where fields genuinely diverge" precedent).
///
/// Code is assigned at Create (not at Approve) -- Contact has no Draft/Approve lifecycle;
/// DocumentType.Contact exists specifically as a "numbering-pool-only" code for this
/// (architecture-spec.md §3.1). The handler generates it via IDocumentNumberGenerator and passes
/// it in here, keeping Domain free of any Infrastructure numbering dependency.
///
/// Deactivate() is a separate method (not folded into Update) -- Contact gets referenced by
/// transactional documents from Phase 5+, so this is a soft delete, matching the roadmap's
/// explicit "Deactivate" command name.
/// </summary>
public sealed class Contact
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public ContactType Type { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string? Address { get; private set; }
    public string? Pan { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public Guid? GroupId { get; private set; }
    public bool IsActive { get; private set; }
    public decimal OpeningBalance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Contact()
    {
    }

    public static Contact Create(
        Guid organizationId,
        ContactType type,
        string name,
        string code,
        string? address,
        string? pan,
        string? phone,
        string? email,
        Guid? groupId,
        decimal openingBalance)
    {
        return new Contact
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Type = type,
            Name = name,
            Code = code,
            Address = address,
            Pan = pan,
            Phone = phone,
            Email = email,
            GroupId = groupId,
            IsActive = true,
            OpeningBalance = openingBalance,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string name,
        string? address,
        string? pan,
        string? phone,
        string? email,
        Guid? groupId,
        decimal openingBalance)
    {
        Name = name;
        Address = address;
        Pan = pan;
        Phone = phone;
        Email = email;
        GroupId = groupId;
        OpeningBalance = openingBalance;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
