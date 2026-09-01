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
            AiDocumentExtractionEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateSettings(
        SuggestSellingPriceMode suggestSellingPriceMode,
        ProductPriceBasis productPriceBasis,
        InventoryTrackingMode inventoryTrackingMode,
        BalanceAction negativeCashBalanceAction,
        BalanceAction negativeStockBalanceAction)
    {
        SuggestSellingPriceMode = suggestSellingPriceMode;
        ProductPriceBasis = productPriceBasis;
        InventoryTrackingMode = inventoryTrackingMode;
        NegativeCashBalanceAction = negativeCashBalanceAction;
        NegativeStockBalanceAction = negativeStockBalanceAction;
    }

    public void SetAccountingDefaults(
        Guid? defaultSalesAccountId,
        Guid? defaultAccountsReceivableId,
        Guid? defaultVatPayableAccountId,
        Guid? defaultPurchaseAccountId,
        Guid? defaultAccountsPayableId,
        Guid? defaultVatReceivableAccountId,
        Guid? defaultTdsPayableAccountId)
    {
        DefaultSalesAccountId = defaultSalesAccountId;
        DefaultAccountsReceivableId = defaultAccountsReceivableId;
        DefaultVatPayableAccountId = defaultVatPayableAccountId;
        DefaultPurchaseAccountId = defaultPurchaseAccountId;
        DefaultAccountsPayableId = defaultAccountsPayableId;
        DefaultVatReceivableAccountId = defaultVatReceivableAccountId;
        DefaultTdsPayableAccountId = defaultTdsPayableAccountId;
    }

    public void SetInventoryDefaults(
        Guid? defaultInventoryAccountId,
        Guid? defaultCogsAccountId,
        Guid? defaultInventoryAdjustmentAccountId)
    {
        DefaultInventoryAccountId = defaultInventoryAccountId;
        DefaultCogsAccountId = defaultCogsAccountId;
        DefaultInventoryAdjustmentAccountId = defaultInventoryAdjustmentAccountId;
    }

    /// <summary>Phase 22 -- turns AI-assisted extraction on or off for this tenant. Its own
    /// mutator rather than a parameter on <see cref="UpdateSettings"/>: the five fields there are
    /// accounting behaviour set once during onboarding, whereas this is a consent decision an
    /// Admin may revisit, and folding it in would mean a routine save of the General settings
    /// screen could silently re-enable data egress.</summary>
    public void SetAiDocumentExtractionEnabled(bool enabled) => AiDocumentExtractionEnabled = enabled;
}
