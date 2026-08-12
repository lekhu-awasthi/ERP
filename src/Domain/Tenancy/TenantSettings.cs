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
        Guid? defaultSalesAccountId, Guid? defaultAccountsReceivableId, Guid? defaultVatPayableAccountId)
    {
        DefaultSalesAccountId = defaultSalesAccountId;
        DefaultAccountsReceivableId = defaultAccountsReceivableId;
        DefaultVatPayableAccountId = defaultVatPayableAccountId;
    }
}
