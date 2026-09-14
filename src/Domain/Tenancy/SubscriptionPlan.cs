namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Phase 41 -- the vendor's plan catalogue: what a tenant can be put on, what it costs, and the two
/// metered ceilings that come with it.
///
/// <para><b>This is the aggregate phase 33 Decision D said not to build, and Decision D's premise
/// was wrong.</b> That decision read a Subscription Amount of <c>0.00</c> and a quota of
/// <c>Standard ( 0 Txn, 0 Products)</c> on the two reference tenants and concluded the three fields
/// were vestigial -- "columns nothing can write and nothing reads". Both tenants were <b>free
/// trials</b>. The vendor's public price list (tiggapp.com/pricing, read 2026-09-14) sells three
/// tiers whose entire commercial difference <i>is</i> those fields: Basic Rs 15,000/yr, Standard
/// Rs 20,000/yr, Professional Rs 32,000/yr, separated by a product ceiling, a transaction ceiling
/// and which entitlements are included. So the fields are not dead; they are the product. This is
/// phase-32's lesson exactly -- a field being dead on the tenant you looked at is a fact about that
/// tenant -- and the cheaper experiment that settled it was reading the seller's price list, not
/// obtaining a third tenant.</para>
///
/// <para><b>Not tenant-scoped, and the only aggregate here that isn't.</b> Every other table in this
/// codebase carries an <c>OrganizationId</c> discriminator. A plan is the <i>vendor's</i> data: the
/// same three rows for every tenant, which no tenant may edit. That is why this type has no
/// organization, why it is seeded through <c>HasData</c> rather than created by a command, and why
/// <c>TenantIndexConvention</c> skips it (its rule is scoped to entities that have an
/// <c>OrganizationId</c> property at all).</para>
///
/// <para><b>The entitlement columns here are descriptive, not operative.</b> They record what the
/// published tier includes so the plan picker can show the same ticks and crosses the price list
/// does. They do <i>not</i> flip <see cref="TenantSubscription"/>'s own flags -- phase 31's
/// <c>Renew</c> decision stands unchanged, and for the reason it gave: letting a billing event
/// re-negotiate what a tenant may model would let a tenant with stock already in a FIFO ledger turn
/// inventory off underneath it. A tier change that should also change entitlements is a vendor
/// action this codebase has no actor for; see docs/phase-41-status.md.</para>
/// </summary>
public sealed class SubscriptionPlan
{
    public Guid Id { get; private set; }

    /// <summary>The stable key a caller names a plan by -- <c>Basic</c>, <c>Standard</c>,
    /// <c>Professional</c>. Unique; the display <see cref="Name"/> may be re-worded, this may not.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>The tier's own one-line positioning, taken verbatim from the published price list so
    /// the picker reads the way the seller's page does.</summary>
    public string Description { get; private set; } = null!;

    /// <summary>Cheapest first, so the catalogue renders in the order the price list shows it
    /// rather than alphabetically (Basic, Professional, Standard).</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>The published list price per year, before VAT and before any add-on. What a given
    /// tenant was actually charged is <see cref="TenantSubscription.SubscriptionAmount"/> -- a
    /// negotiated price, a multi-year discount or an add-on bundle all make the two differ, which
    /// is why the amount is stored on the subscription and not looked up through this row.</summary>
    public decimal AnnualAmount { get; private set; }

    /// <summary>Products the tier includes. Add-ons are sold at Rs 1,000 per additional 1,000, so a
    /// tenant's own ceiling may exceed this -- again stored on the subscription, not here.</summary>
    public int ProductQuota { get; private set; }

    /// <summary>Transactions per subscription year the tier includes; add-ons at Rs 1,000 per
    /// additional 10,000. See <c>DocumentMechanisms.MeteredTransactions</c> for what counts.</summary>
    public int TransactionQuota { get; private set; }

    public bool TrackInventoryIncluded { get; private set; }
    public bool MultipleWarehousesIncluded { get; private set; }
    public bool LandedCostIncluded { get; private set; }
    public bool ManufacturingIncluded { get; private set; }
    public bool PosIncluded { get; private set; }
    public bool MultiCurrencyIncluded { get; private set; }
    public bool DeveloperApiIncluded { get; private set; }

    private SubscriptionPlan()
    {
    }

    /// <summary>
    /// Used only by the seed configuration. There is deliberately no command that creates or edits
    /// a plan: the catalogue belongs to the vendor, and this codebase models exactly one party --
    /// the tenant. Adding a plan is a migration, which is an honest representation of the fact that
    /// changing the price list is currently a deployment.
    /// </summary>
    public static SubscriptionPlan Create(
        Guid id,
        string code,
        string name,
        string description,
        int displayOrder,
        decimal annualAmount,
        int productQuota,
        int transactionQuota,
        bool trackInventoryIncluded,
        bool multipleWarehousesIncluded,
        bool landedCostIncluded,
        bool manufacturingIncluded,
        bool posIncluded,
        bool multiCurrencyIncluded,
        bool developerApiIncluded)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("A subscription plan needs a code.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A subscription plan needs a name.");
        }

        if (annualAmount < 0)
        {
            throw new InvalidOperationException("A subscription plan's annual amount cannot be negative.");
        }

        // Zero is the "not metered" sentinel on a TenantSubscription (phase 31's credit-limit
        // precedent, confirmed live: a stored 0 means no limit). A published plan, though, always
        // states both ceilings -- every tier on the price list names a product and a transaction
        // cap -- so a catalogue row with a zero quota would be a plan that silently sells unlimited
        // use, which is the one mistake here that costs money.
        if (productQuota <= 0 || transactionQuota <= 0)
        {
            throw new InvalidOperationException(
                "A subscription plan must state a positive product and transaction quota; "
                + "zero is the subscription-level 'not metered' sentinel and is not a sellable tier.");
        }

        return new SubscriptionPlan
        {
            Id = id,
            Code = code.Trim(),
            Name = name.Trim(),
            Description = description.Trim(),
            DisplayOrder = displayOrder,
            AnnualAmount = annualAmount,
            ProductQuota = productQuota,
            TransactionQuota = transactionQuota,
            TrackInventoryIncluded = trackInventoryIncluded,
            MultipleWarehousesIncluded = multipleWarehousesIncluded,
            LandedCostIncluded = landedCostIncluded,
            ManufacturingIncluded = manufacturingIncluded,
            PosIncluded = posIncluded,
            MultiCurrencyIncluded = multiCurrencyIncluded,
            DeveloperApiIncluded = developerApiIncluded,
        };
    }
}
