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

    /// <summary>
    /// Phase 31 -- the New/Edit Contact modal's "+ Add More Details" block, live-confirmed
    /// 2026-09-06.
    ///
    /// <para><b>Zero means "no limit", not "a limit of zero".</b> Every existing contact on the
    /// reference tenant reads 0, and one of them sat at 21,800 DR under that 0 with no warning of
    /// any kind; the "Crossed Credit Limit" dialog appeared only once a real figure was saved. A
    /// nullable decimal would have modelled the same thing, but 0-means-unlimited is what the live
    /// field stores and what its number input round-trips, and matching it keeps an imported tenant
    /// from acquiring a limit of zero on every contact at once.</para>
    ///
    /// <para>Enforced at Invoice Approve only, for Customers only -- the tenant setting's own
    /// wording is "when a <i>Customer's</i> balance is about to exceed it's credit limit". The field
    /// is carried on suppliers too because the live form carries it there, but nothing reads it;
    /// see docs/phase-31-status.md Decision B.</para>
    /// </summary>
    public decimal CreditLimit { get; private set; }

    /// <summary>
    /// Phase 31 -- the <c>CreditTerm</c> lookup phase 2 built and nothing has consumed since. It is
    /// a <b>prefill source for a document's Due Date</b>, not a stored rule: the live Invoice form
    /// has its own editable Due Date field and no Credit Terms field at all, and Invoice Age shows
    /// due dates that no listed term could produce. So the term seeds the date when a customer is
    /// picked and is never consulted again -- see <see cref="Sales.Invoice.DueDate"/>.
    /// </summary>
    public Guid? CreditTermId { get; private set; }

    /// <summary>
    /// Phase 31 -- <b>one field, two labels</b>. The live modal renders this control as "Accept
    /// Purchase" while Type is Customer and as "Accept Sales" while Type is Supplier: in both cases
    /// it means "this contact may also be transacted with in the opposite role". Modelling it as
    /// two booleans would have been modelling the labels rather than the field.
    ///
    /// <para>Its effect is on <i>pickers</i>: with it on, a Customer is offered on Purchase
    /// documents and a Supplier on Sales documents. It is deliberately not an approval-time gate --
    /// turning it off must not invalidate documents already written against the contact.</para>
    ///
    /// <para>Always false for a <see cref="ContactType.Lead"/>, enforced in
    /// <see cref="Create"/>/<see cref="Update"/>: on the live form a Lead has no additional-details
    /// block at all -- no PAN, no toggle, no credit term, no credit limit.</para>
    /// </summary>
    public bool AcceptsReverseTransactions { get; private set; }

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
        decimal openingBalance,
        decimal creditLimit = 0m,
        Guid? creditTermId = null,
        bool acceptsReverseTransactions = false)
    {
        var isLead = type == ContactType.Lead;

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
            CreditLimit = isLead ? 0m : creditLimit,
            CreditTermId = isLead ? null : creditTermId,
            AcceptsReverseTransactions = !isLead && acceptsReverseTransactions,
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
        decimal openingBalance,
        decimal creditLimit = 0m,
        Guid? creditTermId = null,
        bool acceptsReverseTransactions = false)
    {
        var isLead = Type == ContactType.Lead;

        Name = name;
        Address = address;
        Pan = pan;
        Phone = phone;
        Email = email;
        GroupId = groupId;
        OpeningBalance = openingBalance;
        CreditLimit = isLead ? 0m : creditLimit;
        CreditTermId = isLead ? null : creditTermId;
        AcceptsReverseTransactions = !isLead && acceptsReverseTransactions;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
