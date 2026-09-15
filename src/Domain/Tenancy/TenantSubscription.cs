namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Read-mostly per-tenant subscription (architecture-spec.md §4.1): a 15-day trial seeded at
/// Organization creation (confirmed live in erp-module-scan.md's Signup & Onboarding section),
/// plus the entitlement flags gating command availability elsewhere -- the Step 2 "Accounting
/// Features" wizard checkboxes become these flags, one-time opt-ins made at creation.
/// </summary>
public sealed class TenantSubscription
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>
    /// Phase 41 -- the catalogue row this tenant was put on, or <c>null</c> while it is still on the
    /// seeded trial (and for any tenant whose plan predates this phase).
    /// </summary>
    public Guid? PlanId { get; private set; }

    /// <summary>
    /// The plan's name <b>as it was at the time it was sold</b>, not a live read through
    /// <see cref="PlanId"/>. Kept denormalised on purpose: re-wording a catalogue row must not
    /// silently restate what an existing tenant bought. Before phase 41 this was free text typed
    /// into the Renew box; it is now always either "Trial" or a catalogue plan's name.
    /// </summary>
    public string PlanName { get; private set; } = null!;

    /// <summary>
    /// When this tenant began -- untouched by every renewal, because it records the tenant's origin
    /// and a renewal does not change it (phase 31). Called <c>TrialStartsAt</c> until phase 43; the
    /// trial is only what the origin happened to start, and a tenant that signed straight onto a
    /// paid plan never had one.
    /// </summary>
    public DateTimeOffset OriginatedAt { get; private set; }

    /// <summary>
    /// Phase 41 -- the start of the <b>current</b> term, which is what the transaction quota is
    /// counted over. Seeded equal to <see cref="OriginatedAt"/> and moved forward by every
    /// <see cref="SetPlan"/>.
    ///
    /// <para><b>Why a second date rather than counting from <see cref="OriginatedAt"/>.</b> The
    /// quota the price list sells is "transactions / year" against a term that is bought and
    /// re-bought; counting from the tenant's origin would meter a five-year-old tenant against the
    /// allowance it purchased this year plus everything it has ever done. Counting backwards a fixed
    /// year from the end date would silently re-open the allowance whenever an end date moved. The
    /// honest window is the term actually paid for, which needs both of its ends stored.</para>
    /// </summary>
    public DateTimeOffset TermStartsAt { get; private set; }

    /// <summary>
    /// The instant past which <c>SubscriptionExpiryBehavior</c> makes this organization read-only
    /// for business documents -- the end of whatever term is current, trial or paid.
    ///
    /// <para><b>Phase 43 renamed this from <c>TrialEndsAt</c></b> (phase-41 carried item #2). It has
    /// described paid terms since phase 41 recorded one, and a column whose name says trial is a
    /// standing invitation to reason about it as one. <c>OriginatedAt</c> was renamed in the same
    /// pass and deliberately <i>not</i> to a term date: it is the tenant's origin and moves for
    /// nothing, which is precisely what <see cref="TermStartsAt"/> is not. Neither rename changed
    /// what <c>SubscriptionUsageReader</c> or <c>SubscriptionQuotaBehavior</c> computes -- the
    /// window is still <see cref="TermStartsAt"/>..this.
    /// </summary>
    public DateTimeOffset TermEndsAt { get; private set; }

    /// <summary>
    /// Phase 41 -- what this tenant is actually charged for the current term, which is not
    /// necessarily its plan's list price: a negotiated rate, a multi-year discount and the add-on
    /// bundle all land here. <c>0</c> on a trial, which is exactly what both reference tenants
    /// showed and what phase 33 mistook for a dead column.
    /// </summary>
    public decimal SubscriptionAmount { get; private set; }

    /// <summary>
    /// Phase 41 -- the ceiling on products, <b>including any add-on</b> (sold at Rs 1,000 per
    /// additional 1,000), which is why it is stored here rather than read through the plan.
    ///
    /// <para><b>Zero means not metered</b>, following phase 31's credit limit -- a stored 0 means no
    /// limit, confirmed live there. That is what makes a trial usable: a trial has no purchased
    /// allowance, and a trial that could create no products at all would be worthless. It is also
    /// the reading that makes the live <c>Standard ( 0 Txn, 0 Products)</c> coherent on two
    /// unmetered trial tenants.</para>
    /// </summary>
    public int ProductQuota { get; private set; }

    /// <summary>
    /// Phase 41 -- the ceiling on metered transactions per term, including any add-on (Rs 1,000 per
    /// additional 10,000). Zero means not metered, as for <see cref="ProductQuota"/>. What counts is
    /// <c>DocumentMechanisms.MeteredTransactions</c>, and the window is
    /// <see cref="TermStartsAt"/>..<see cref="TermEndsAt"/>.
    /// </summary>
    public int TransactionQuota { get; private set; }

    /// <summary>
    /// Phase 46 -- the ceiling on AI document extractions per <b>Nepal-local day</b>. Zero means not
    /// metered, as for the two above.
    ///
    /// <para><b>Per day, and not purchasable.</b> Every published tier states "Up to 20 scans per
    /// day" and the add-on collection sells no extra scans, so this is the one metered axis that is
    /// not a commercial lever. What it is instead is a rate limit on the only action in this product
    /// that spends money outward per call (phase 22's <c>IDocumentExtractor</c>), which is why it is
    /// worth enforcing even though nobody is selling it.</para>
    ///
    /// <para><b>Unlike the other two, a trial is metered.</b> Phase 41's zero-means-unmetered is
    /// right for an allowance somebody <i>bought</i> -- a trial that could create no products would
    /// be a trial of nothing. It is exactly wrong here: unmetered would mean a free trial with
    /// uncapped spend on a paid API. So <see cref="CreateTrial"/> seeds the published 20 rather than
    /// the sentinel, and the sentinel stays available for a tenant deliberately exempted.</para>
    /// </summary>
    public int DailyAiScanQuota { get; private set; }

    /// <summary>
    /// Phase 46 -- how many billing locations this tenant has <b>paid for</b> (Rs 5,000 per location
    /// per year on the published add-on list). Zero means no purchased count has been recorded.
    ///
    /// <para><b>A record, not a ceiling -- and that distinction was settled by reading the live
    /// product, against this phase's own plan.</b> Cadehi runs three locations with Billing Location
    /// simply <i>Enabled</i>: the list is unbounded, the Add New Location dialog states no cap, no
    /// remaining count and no charge, and the Subscriptions screen shows no location row at all. So
    /// the reference product bills per location commercially and enforces nothing, and
    /// <c>CreateBillingLocationCommandHandler</c> keeps phase 32's boolean cap-at-one as the only
    /// refusal. Building a refusing ceiling here would have shipped an enforcement the product
    /// demonstrably does not have -- phase 43's product-to-location precedent, arrived at the same
    /// way.</para>
    ///
    /// <para>It is stored anyway because the commercial record is otherwise incomplete: without it
    /// nothing says a tenant running three locations bought three, and the subscription screen can
    /// show what was sold beside what is in use.</para>
    /// </summary>
    public int LocationQuota { get; private set; }

    /// <summary>
    /// Phase 41 -- the IRD Billing add-on (Rs 15,000, one-time on the published price list), shown
    /// as the reference product's own <b>IRD Verified</b> row. Distinct from
    /// <see cref="IrdSyncEnabled"/>, which is the integration switch: a tenant can have paid for
    /// verification long before any sync exists to enable, which is this codebase's situation
    /// exactly.
    /// </summary>
    public bool IrdVerified { get; private set; }
    public bool TrackInventoryEnabled { get; private set; }
    public bool MultipleLocationsEnabled { get; private set; }
    public bool MultipleWarehousesEnabled { get; private set; }
    public bool MultiCurrencyEnabled { get; private set; }
    public bool ManufacturingEnabled { get; private set; }
    public bool PosRetailEnabled { get; private set; }
    public bool PosRestaurantEnabled { get; private set; }
    // Reserved per architecture-spec.md §4.1/§6 -- no IRD e-filing integration designed yet,
    // so this can never be enabled at creation; a later phase flips it on once that's built.
    public bool IrdSyncEnabled { get; private set; }

    /// <summary>
    /// The published per-day AI scan allowance, identical on Basic, Standard and Professional and on
    /// every term length (tiggapp.com/pricing, read 2026-09-15). Named here because a trial has no
    /// plan row to read it from, and because the migration that backfills existing tenants must use
    /// the same number the seeded catalogue does.
    /// </summary>
    public const int DefaultDailyAiScanQuota = 20;

    private TenantSubscription()
    {
    }

    public static TenantSubscription CreateTrial(Guid organizationId, AccountingFeatureSelections features)
    {
        var now = DateTimeOffset.UtcNow;

        return new TenantSubscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanName = "Trial",
            OriginatedAt = now,
            TermStartsAt = now,
            TermEndsAt = now.AddDays(15),
            // A trial is deliberately unmetered on both axes: no allowance has been purchased, and
            // a trial that refused to create a product or approve an invoice would be a trial of
            // nothing. Zero is the same "no limit" sentinel phase 31 confirmed live for a credit
            // limit, and it is what the two reference tenants' own "( 0 Txn, 0 Products)" shows.
            PlanId = null,
            SubscriptionAmount = 0m,
            ProductQuota = 0,
            TransactionQuota = 0,
            // Not the sentinel, deliberately -- see DailyAiScanQuota. A trial is the case where an
            // uncapped paid-API call matters most, and 20 is what every published tier grants.
            DailyAiScanQuota = DefaultDailyAiScanQuota,
            LocationQuota = 0,
            IrdVerified = false,
            TrackInventoryEnabled = features.TrackInventory,
            MultipleLocationsEnabled = features.MultipleLocations,
            MultipleWarehousesEnabled = features.MultipleWarehouses,
            MultiCurrencyEnabled = features.MultiCurrency,
            ManufacturingEnabled = features.Manufacturing,
            PosRetailEnabled = features.PosRetail,
            PosRestaurantEnabled = features.PosRestaurant,
            IrdSyncEnabled = false,
        };
    }

    /// <summary>
    /// Phase 31's <c>Renew</c>, widened by phase 41 into "record the term that was sold": the plan,
    /// what it cost, the two ceilings it came with, the IRD Billing add-on, and the window
    /// (<see cref="TermStartsAt"/>..<see cref="TermEndsAt"/>) the transaction ceiling is counted
    /// over. It remains the one mutator, and the one command an expired organization can still run.
    ///
    /// <para><b>It still does not touch the entitlement flags</b>, and phase 31's reason is
    /// unchanged: a billing event is not a re-negotiation of what the tenant may model, and letting
    /// it flip <c>TrackInventoryEnabled</c> would let a tenant with stock already in a FIFO ledger
    /// turn inventory off underneath it. A plan's own <c>...Included</c> columns are therefore
    /// descriptive -- they say what the tier publishes, and a mismatch between a tier and this
    /// tenant's flags is real and visible rather than silently reconciled. Making a tier change
    /// carry entitlements is a <i>vendor</i> action, and this codebase has no vendor actor; see
    /// docs/phase-41-status.md.</para>
    ///
    /// <para><see cref="OriginatedAt"/> is likewise untouched: it records when this tenant began,
    /// which a renewal does not change. <see cref="TermStartsAt"/> is what moves.</para>
    ///
    /// <param name="termStartsAt">The start of the term being recorded. Callers pass "now" for a
    /// renewal taking effect today; it is a parameter rather than <c>UtcNow</c> so that back-dating
    /// a term that was paid for earlier is expressible, and so the Domain stays clock-free.</param>
    /// </summary>
    public void SetPlan(
        Guid? planId,
        string planName,
        DateTimeOffset termStartsAt,
        DateTimeOffset endsAt,
        decimal subscriptionAmount,
        int productQuota,
        int transactionQuota,
        int dailyAiScanQuota,
        int locationQuota,
        bool irdVerified)
    {
        if (string.IsNullOrWhiteSpace(planName))
        {
            throw new InvalidOperationException("A subscription needs a plan name.");
        }

        if (endsAt <= OriginatedAt)
        {
            throw new InvalidOperationException("A subscription cannot end before it started.");
        }

        if (endsAt <= termStartsAt)
        {
            throw new InvalidOperationException("A subscription term cannot end before it begins.");
        }

        if (subscriptionAmount < 0)
        {
            throw new InvalidOperationException("A subscription amount cannot be negative.");
        }

        // Zero is the "not metered" sentinel; anything below it is neither a ceiling nor a
        // sentinel, and would read as "unlimited" through every comparison that follows.
        if (productQuota < 0 || transactionQuota < 0 || dailyAiScanQuota < 0)
        {
            throw new InvalidOperationException(
                "A subscription quota cannot be negative; zero means not metered.");
        }

        // Not a ceiling and never enforced (see LocationQuota), but a negative count of purchased
        // locations is not a record of anything either.
        if (locationQuota < 0)
        {
            throw new InvalidOperationException("A purchased location count cannot be negative.");
        }

        PlanId = planId;
        PlanName = planName.Trim();
        TermStartsAt = termStartsAt;
        TermEndsAt = endsAt;
        SubscriptionAmount = subscriptionAmount;
        ProductQuota = productQuota;
        TransactionQuota = transactionQuota;
        DailyAiScanQuota = dailyAiScanQuota;
        LocationQuota = locationQuota;
        IrdVerified = irdVerified;
    }

    /// <summary>
    /// Whether this tenant opted into <paramref name="feature"/> at Organization creation
    /// (FR-2.6, enforced since Phase 20f). The single place the <see cref="TenantFeature"/> enum
    /// maps back onto the flag columns, so FeatureGateBehavior and the read-only subscription
    /// query can't disagree about which column a feature means.
    ///
    /// These flags are immutable after creation, matching the reference product: its own
    /// Configurations &gt; Tigg Subscriptions screen renders them as read-only rows, and a
    /// disabled feature's panel says to contact vendor support to activate it -- there is no
    /// self-service toggle. See phase-20f-status.md's confirm-live findings.
    /// </summary>
    public bool IsEnabled(TenantFeature feature)
    {
        return feature switch
        {
            TenantFeature.TrackInventory => TrackInventoryEnabled,
            TenantFeature.MultipleLocations => MultipleLocationsEnabled,
            TenantFeature.MultipleWarehouses => MultipleWarehousesEnabled,
            TenantFeature.MultiCurrency => MultiCurrencyEnabled,
            TenantFeature.Manufacturing => ManufacturingEnabled,
            TenantFeature.PosRetail => PosRetailEnabled,
            TenantFeature.PosRestaurant => PosRestaurantEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown tenant feature."),
        };
    }
}
