namespace ErpApp.Domain.Tenancy;

/// <summary>Recent Selling Price vs Fixed Selling Price (erp-module-scan.md's Configurations > General).</summary>
public enum SuggestSellingPriceMode
{
    RecentSellingPrice,
    FixedSellingPrice,
}

/// <summary>Inclusive of VAT vs Exclusive of VAT (erp-module-scan.md's Configurations > General).</summary>
public enum ProductPriceBasis
{
    InclusiveOfVat,
    ExclusiveOfVat,
}

/// <summary>
/// Physical Movement (Delivery Note/GRN-based) vs Accounting Movement (Invoice/PurchaseBill
/// directly) -- architecture-spec.md §3.5 flags this as "the exact seam where FIFO layers get
/// touched" and recommends v1 ship AccountingMovement only (matches both scanned tenants).
/// </summary>
public enum InventoryTrackingMode
{
    PhysicalMovement,
    AccountingMovement,
}

/// <summary>Shared by NegativeCashBalanceAction/NegativeStockBalanceAction (architecture-spec.md §3.5).</summary>
public enum BalanceAction
{
    Reject,
    Warn,
    DoNothing,
}

/// <summary>
/// How far a tenant's billing locations reach across its documents -- the radio/checkbox pair inside
/// Organization &gt; Features &gt; Billing Location &gt; <b>Advanced</b> ("Choose how locations should
/// be used in your organization"), confirmed live 2026-09-07 and invisible on a tenant without the
/// entitlement, which is why no earlier phase could see it.
///
/// <para>The live wording is verbatim: <see cref="SalesTransactionsOnly"/> is
/// <i>"Enable Location in Sales Transactions Only -- Use locations only in sales-related
/// transactions. (Invoice, Sales Order, POS, Credit Note)"</i> and carries a <b>Default</b> badge;
/// <see cref="AllTransactions"/> is <i>"Enable Location in All Transactions -- Apply location
/// tracking across all transaction modules. (Sales, Purchase, Inventory, Accounting, etc.)"</i>.
///
/// <para><b>This is why <c>LocationId</c> is nullable on every transactional aggregate rather than
/// present only on the sales four.</b> The scope is a runtime setting an Admin can widen at any
/// moment, so the column has to exist on the purchase, inventory and accounting documents before
/// anybody flips it -- otherwise the switch would be a lie until some later phase shipped the schema.
/// See <c>DocumentLocationScope</c> for the single place that turns this enum into a per-DocumentType
/// answer.</para>
/// </summary>
public enum LocationScopeMode
{
    /// <summary>The live default. Its member list is taken verbatim from the label's own
    /// parenthesis, nothing added -- see <c>DocumentLocationScope</c>.</summary>
    SalesTransactionsOnly = 0,

    AllTransactions = 1,
}

/// <summary>
/// Single-row-per-tenant settings aggregate. Seeded with sensible defaults at Organization
/// creation (roadmap Phase 1b task 3) so it always exists by the time Phase 2 adds the real
/// configurable fields (Suggest Selling Price mode, Product Price Basis, Inventory Tracking
/// mode, Negative Cash/Stock Balance actions -- architecture-spec.md §4.10). Defaults mirror
/// erp-module-scan.md's confirmed live tenant behavior: RecentSellingPrice, ExclusiveOfVat,
/// AccountingMovement (v1 scope per §3.5), NegativeCashBalanceAction=Reject ("Reject confirmed"),
/// NegativeStockBalanceAction=Warn (confirmed live Warn-and-allow).
/// </summary>
public sealed class TenantSettings
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public SuggestSellingPriceMode SuggestSellingPriceMode { get; private set; }
    public ProductPriceBasis ProductPriceBasis { get; private set; }
    public InventoryTrackingMode InventoryTrackingMode { get; private set; }
    public BalanceAction NegativeCashBalanceAction { get; private set; }
    public BalanceAction NegativeStockBalanceAction { get; private set; }

    /// <summary>
    /// Phase 31 (FR-3.x credit control) -- the third member of the Reject/Warn/DoNothing family,
    /// live-confirmed 2026-09-06 as a peer of the other two on Configurations &gt; General ("Select a
    /// action to be triggered when a Customer's balance is about to exceed it's credit limit").
    ///
    /// <para>Consulted at <b>Invoice Approve</b> and nowhere else. Two live facts pin that: saving a
    /// breaching draft produced no warning at all, and pressing Approve raised a "Crossed Credit
    /// Limit" dialog with Dismiss/Continue -- the same shape as the Negative Stock Balance dialog,
    /// which is why this reuses <see cref="BalanceAction"/> rather than inventing a parallel enum.
    /// The setting's own wording says <i>Customer's</i> balance, so a supplier's credit limit is
    /// carried on the Contact but is not enforced anywhere; see docs/phase-31-status.md Decision B.
    /// </para>
    ///
    /// <para>Defaults to <see cref="BalanceAction.Warn"/>, matching NegativeStockBalanceAction
    /// rather than NegativeCashBalanceAction's Reject: a credit limit is a commercial judgement a
    /// salesperson may legitimately override, where an overdrawn bank account is an accounting
    /// error. The default is inert on a fresh tenant regardless, because
    /// <see cref="Domain.Contacts.Contact.CreditLimit"/> is 0 (= no limit) until somebody sets
    /// one.</para>
    /// </summary>
    public BalanceAction CreditLimitExceedsAction { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Phase 5 addition -- fallback GL accounts read by InvoicePostingRule/PaymentPostingRule
    /// when a line's Product doesn't carry its own SalesAccountId (Product.SalesAccountId/etc.,
    /// Phase 4-backfilled but still commonly unset for a while after onboarding). All three null
    /// until an Admin sets them via SetAccountingDefaults -- Approve fails with a clear error if a
    /// posting rule needs one and neither the Product nor this fallback has it, rather than
    /// guessing. See phase-5-status.md's scope decision for why TenantSettings-level fallback was
    /// chosen over forcing every Product to carry GL accounts before it can ever be invoiced.
    /// </summary>
    public Guid? DefaultSalesAccountId { get; private set; }
    public Guid? DefaultAccountsReceivableId { get; private set; }
    public Guid? DefaultVatPayableAccountId { get; private set; }

    /// <summary>
    /// Phase 6 addition -- the Purchase-side mirror of the three fields above, read by
    /// PurchaseBillPostingRule/ExpensePostingRule. DefaultVatReceivableAccountId is deliberately a
    /// separate field from DefaultVatPayableAccountId -- Purchase VAT is an input-tax-credit
    /// (receivable) account, not the same control account Sales' output VAT posts to, a real
    /// accounting distinction (see phase-6-status.md's scope decisions).
    /// </summary>
    public Guid? DefaultPurchaseAccountId { get; private set; }
    public Guid? DefaultAccountsPayableId { get; private set; }
    public Guid? DefaultVatReceivableAccountId { get; private set; }
    public Guid? DefaultTdsPayableAccountId { get; private set; }

    /// <summary>
    /// Phase 7 addition -- Product carries no InventoryAccountId/CogsAccountId of its own (only
    /// the Sales/Purchase account quartet from Phases 4-6 exist), so InvoicePostingRule's COGS leg
    /// (Debit COGS / Credit Inventory, using the FIFO-computed cost of what Approve just consumed)
    /// falls back to these tenant-level defaults exactly like DefaultSalesAccountId/etc. do --
    /// same pattern, one level up since there's no per-Product override to fall back from. Not
    /// confirmed against the reference product (erp-module-scan.md doesn't cover a COGS posting
    /// leg) -- see phase-7-status.md's scope decisions. DefaultInventoryAdjustmentAccountId is the
    /// Variance/Adjustment contra account InventoryAdjustmentPostingRule posts the offsetting leg
    /// to (see that rule's doc comment for the net-balanced-effect reasoning).
    /// </summary>
    public Guid? DefaultInventoryAccountId { get; private set; }
    public Guid? DefaultCogsAccountId { get; private set; }
    public Guid? DefaultInventoryAdjustmentAccountId { get; private set; }

    /// <summary>
    /// Phase 25 addition (FR-8.9) -- the eleventh and only new default account, read solely by
    /// ProductionJournalPostingRule. It is the contra-expense (absorption) account a production
    /// run credits when it capitalises labour/overhead into finished-goods stock: Inventory is
    /// debited with the new layers and credited with the consumed ones, and this account takes the
    /// difference, which is exactly the production expenses added.
    ///
    /// <para>Deliberately <b>not</b> a reuse of DefaultInventoryAdjustmentAccountId: production is
    /// not an adjustment, and folding the two together would make the Inventory Adjustment
    /// account's balance unreadable as either. Deliberately not a WIP control account either --
    /// a Production Journal is atomic, so a WIP account would be debited and credited within the
    /// same entry and always net to zero, adding an eleventh account for no information.</para>
    ///
    /// <para>Required at Approve, unconditionally, exactly as DefaultInventoryAccountId is: the
    /// posting leg can be non-zero even with no expense lines, because the finished unit cost is
    /// rounded to the stock ledger's own scale. See ApproveProductionJournalCommandHandler.</para>
    /// </summary>
    public Guid? DefaultProductionCostAccountId { get; private set; }

    /// <summary>
    /// Phase 22 addition (FR-10.3) -- the tenant's opt-in to AI-assisted extraction on Document
    /// inbox uploads. <b>Default false, and deliberately so:</b> this is the first feature in the
    /// product that sends tenant business documents to a third party, and an opt-out default would
    /// mean the egress started the day the migration ran, with nobody having agreed to it.
    ///
    /// <para>Not a <see cref="TenantFeature"/>: those are captured once at Organization creation
    /// from the signup wizard and are immutable afterwards (see
    /// <see cref="TenantSubscription.IsEnabled"/>), which is exactly wrong for a consent decision a
    /// tenant must be able to withdraw. Nothing else about the Document inbox is gated on this --
    /// upload, manual conversion, linking and viewing all work identically with it off, so a tenant
    /// that never turns it on still has the whole of FR-10.3's inbox (Phase 20f's lesson: check
    /// that a flag-off tenant can still function). See docs/phase-22-status.md, Decision C.</para>
    /// </summary>
    public bool AiDocumentExtractionEnabled { get; private set; }

    /// <summary>
    /// Phase 28 (FR-2.5) -- the two accounts the realised exchange difference on a payment
    /// allocation is booked to (see ForexPostingRule). Read only when a foreign-currency
    /// settlement actually produces a difference, so a single-currency tenant never has to set
    /// them and never sees an error about them.
    ///
    /// <para><b>Two accounts, not one, and this diverges from the reference product on purpose.</b>
    /// Its chart of accounts ships exactly one forex account -- "Forex Gain" (Income, under a
    /// "Foreign Exchange Gain" group) -- and no loss counterpart at all (confirmed live 2026-09-04
    /// by searching both the account and the group lists). Netting losses into that same Income
    /// account would give an Income-type account a debit balance, which every statement in
    /// docs/phase-8a-status.md's family presents with the wrong sign. This is the same call
    /// phase 6 made in keeping DefaultVatReceivableAccountId separate from
    /// DefaultVatPayableAccountId: one real accounting distinction is worth one more nullable
    /// column.</para>
    /// </summary>
    public Guid? DefaultForexGainAccountId { get; private set; }

    /// <inheritdoc cref="DefaultForexGainAccountId"/>
    public Guid? DefaultForexLossAccountId { get; private set; }

    /// <summary>
    /// Phase 29 (FR-6.15) -- the account a Purchase Bill's capitalised Additional Cost is credited
    /// to, against a debit to <see cref="DefaultInventoryAccountId"/>. A clearing (liability)
    /// account: the freight, duty or insurance is owed to somebody, and when that somebody's own
    /// bill is entered against this same account the two net to zero.
    ///
    /// <para><b>Why a clearing account and not the supplier.</b> Confirmed live 2026-09-04 on two
    /// already-approved reference bills: an Additional Cost row is <i>not</i> added to the bill's
    /// Grand Total and the supplier's payable is credited the goods total only, and the row has no
    /// payee field to name anybody else with. So the credit cannot be a payable to a party this
    /// document knows about. (The reference product posts nothing at all -- it is periodic, so the
    /// landed cost lives only in its stock-costing subsystem. We are perpetual, and the same
    /// argument as phase-25 Decision A applies: posting nothing would leave the Inventory account
    /// understating stock by exactly the capitalised cost, permanently and silently.)</para>
    ///
    /// <para>Resolved <b>only when a bill actually carries an additional cost</b>, never up front --
    /// the same lazy treatment as the two forex accounts above, and for the same reason: demanding
    /// it at Approve regardless would make every tenant configure an account for a feature most
    /// never touch.</para>
    /// </summary>
    public Guid? DefaultLandedCostClearingAccountId { get; private set; }

    /// <summary>
    /// Phase 32 (FR-2.3/FR-3.3) -- the first of the two controls inside Organization &gt; Features &gt;
    /// Billing Location &gt; Advanced. Defaults to <see cref="LocationScopeMode.SalesTransactionsOnly"/>,
    /// which is the option the live screen badges <b>Default</b>.
    ///
    /// <para>Lives on TenantSettings rather than on <see cref="TenantSubscription"/> beside the
    /// MultipleLocations entitlement, and the distinction matters: the entitlement is bought, captured
    /// once at Organization creation and immutable afterwards
    /// (<see cref="TenantSubscription.IsEnabled"/>), whereas this is a configuration decision an Admin
    /// revisits -- exactly the line phase 22 drew for
    /// <see cref="AiDocumentExtractionEnabled"/>.</para>
    /// </summary>
    public LocationScopeMode LocationScopeMode { get; private set; }

    /// <summary>
    /// Phase 32 -- the second Advanced control, live-labelled <i>"Implement Location Wise Permission
    /// for Report View -- Restrict users to view reports only for locations they have access to."</i>
    ///
    /// <para><b>Stored and editable here, but read by nothing yet, and that is a deliberate scope
    /// line rather than an oversight.</b> The live role editor holds the answer to what it does: its
    /// Location-specific Permissions section replicates <i>only</i> the Transactions group (94 keys)
    /// per location, while General/Settings/<b>Reports</b> stay organization-wide. Turning this on is
    /// what would pull the 52 Reports keys into location scope -- so its consumer is the per-location
    /// permission matrix, which is phase 32b. It ships now because it is one of the three controls on
    /// the Advanced panel and a panel with a control missing is the phase-31 trap in reverse.</para>
    ///
    /// <para>Phase-31 lesson (a) applies and is answered: a setting with no command behind it is an
    /// absent feature, so this one has its command
    /// (<c>UpdateBillingLocationSettingsCommand</c>), its endpoint and its screen from day one. What
    /// it lacks is an <i>enforcer</i>, which is named here and in docs/phase-32-status.md rather than
    /// left to be discovered.</para>
    /// </summary>
    public bool LocationWiseReportPermission { get; private set; }

    private TenantSettings()
    {
    }

    public static TenantSettings CreateDefault(Guid organizationId)
    {
        return new TenantSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SuggestSellingPriceMode = SuggestSellingPriceMode.RecentSellingPrice,
            ProductPriceBasis = ProductPriceBasis.ExclusiveOfVat,
            InventoryTrackingMode = InventoryTrackingMode.AccountingMovement,
            NegativeCashBalanceAction = BalanceAction.Reject,
            NegativeStockBalanceAction = BalanceAction.Warn,
            CreditLimitExceedsAction = BalanceAction.Warn,
            AiDocumentExtractionEnabled = false,
            LocationScopeMode = LocationScopeMode.SalesTransactionsOnly,
            LocationWiseReportPermission = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateSettings(
        SuggestSellingPriceMode suggestSellingPriceMode,
        ProductPriceBasis productPriceBasis,
        InventoryTrackingMode inventoryTrackingMode,
        BalanceAction negativeCashBalanceAction,
        BalanceAction negativeStockBalanceAction,
        BalanceAction creditLimitExceedsAction)
    {
        SuggestSellingPriceMode = suggestSellingPriceMode;
        ProductPriceBasis = productPriceBasis;
        InventoryTrackingMode = inventoryTrackingMode;
        NegativeCashBalanceAction = negativeCashBalanceAction;
        NegativeStockBalanceAction = negativeStockBalanceAction;
        CreditLimitExceedsAction = creditLimitExceedsAction;
    }

    public void SetAccountingDefaults(
        Guid? defaultSalesAccountId,
        Guid? defaultAccountsReceivableId,
        Guid? defaultVatPayableAccountId,
        Guid? defaultPurchaseAccountId,
        Guid? defaultAccountsPayableId,
        Guid? defaultVatReceivableAccountId,
        Guid? defaultTdsPayableAccountId,
        Guid? defaultForexGainAccountId = null,
        Guid? defaultForexLossAccountId = null)
    {
        DefaultSalesAccountId = defaultSalesAccountId;
        DefaultAccountsReceivableId = defaultAccountsReceivableId;
        DefaultVatPayableAccountId = defaultVatPayableAccountId;
        DefaultPurchaseAccountId = defaultPurchaseAccountId;
        DefaultAccountsPayableId = defaultAccountsPayableId;
        DefaultVatReceivableAccountId = defaultVatReceivableAccountId;
        DefaultTdsPayableAccountId = defaultTdsPayableAccountId;
        DefaultForexGainAccountId = defaultForexGainAccountId;
        DefaultForexLossAccountId = defaultForexLossAccountId;
    }

    public void SetInventoryDefaults(
        Guid? defaultInventoryAccountId,
        Guid? defaultCogsAccountId,
        Guid? defaultInventoryAdjustmentAccountId,
        Guid? defaultProductionCostAccountId,
        Guid? defaultLandedCostClearingAccountId = null)
    {
        DefaultInventoryAccountId = defaultInventoryAccountId;
        DefaultCogsAccountId = defaultCogsAccountId;
        DefaultInventoryAdjustmentAccountId = defaultInventoryAdjustmentAccountId;
        DefaultProductionCostAccountId = defaultProductionCostAccountId;
        DefaultLandedCostClearingAccountId = defaultLandedCostClearingAccountId;
    }

    /// <summary>Phase 22 -- turns AI-assisted extraction on or off for this tenant. Its own
    /// mutator rather than a parameter on <see cref="UpdateSettings"/>: the five fields there are
    /// accounting behaviour set once during onboarding, whereas this is a consent decision an
    /// Admin may revisit, and folding it in would mean a routine save of the General settings
    /// screen could silently re-enable data egress.</summary>
    public void SetAiDocumentExtractionEnabled(bool enabled) => AiDocumentExtractionEnabled = enabled;

    /// <summary>
    /// Phase 32 -- the Advanced panel on Organization &gt; Features &gt; Billing Location. Its own
    /// mutator rather than two more parameters on <see cref="UpdateSettings"/>, on the phase-22
    /// precedent directly above: those six fields are Configurations &gt; General's screen, these two
    /// are a different screen in a different module, and folding them together would mean a routine
    /// save of the General settings page could silently narrow which documents carry a location.
    /// </summary>
    public void SetLocationSettings(LocationScopeMode locationScopeMode, bool locationWiseReportPermission)
    {
        LocationScopeMode = locationScopeMode;
        LocationWiseReportPermission = locationWiseReportPermission;
    }
}
