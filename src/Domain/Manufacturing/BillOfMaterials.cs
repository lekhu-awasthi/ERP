namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// The manufacturing "recipe" (FR-8.8, erp-module-scan.md Inventory §8), confirmed live on
/// 2026-09-02: a BOM names one finished-good Product, an Output Quantity, and three child
/// collections -- raw materials consumed, by-products co-produced with a % cost allocation, and
/// production expense terms.
///
/// <para><b>A BOM is a template, never a constraint (docs/phase-25-status.md Decision D).</b> The
/// reference product exposes it as an explicit "LOAD BOM" button on the Production Order/Journal
/// forms which appears only once a Product and an Output Quantity are both set, and which fills
/// editable line rows scaled by (this document's Output Quantity / the BOM's own OutputQuantity).
/// Nothing afterwards re-checks a document against its BOM. That is the same defaults-not-enforces
/// contract every Get*ConversionTemplateQuery in this codebase already has, and it is what lets a
/// Production Journal record what actually happened -- wastage, substitution and all -- which is
/// the entire point of an execution document.</para>
///
/// <para>Master data, so no Draft/Approve lifecycle and no document number: this is the
/// ProductCategory/UnitOfMeasurement shape, not the ApprovableTransaction shape. At most one BOM
/// per finished product (a unique index on (OrganizationId, ProductId)) -- live-confirmed by the
/// absence of any BOM picker on "LOAD BOM": the form resolves the BOM from the chosen product
/// alone, so a second BOM for the same product could never be reached.</para>
/// </summary>
public sealed class BillOfMaterials
{
    private readonly List<BomRawMaterialLine> _rawMaterials = [];
    private readonly List<BomByProductLine> _byProducts = [];
    private readonly List<BomExpenseLine> _expenses = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal OutputQuantity { get; private set; }

    /// <summary>
    /// Live-confirmed as a real checkbox on the BOM form ("Manufacture on every sales."), and
    /// <b>deliberately stored but not honoured</b> (docs/phase-25-status.md Decision D). Auto-raising
    /// a Production Journal inside ApproveInvoiceCommandHandler would create an un-numbered,
    /// un-permissioned, costed document with no human on the form -- the exact shape phase-22's
    /// Decision B rejected. Kept as a field so a BOM edited here round-trips it rather than losing
    /// it, and labelled in the UI as recorded-only, per phase-21b's Decision A precedent of saying
    /// so on the control rather than shipping the word.
    /// </summary>
    public bool ManufactureOnEverySale { get; private set; }

    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<BomRawMaterialLine> RawMaterials => _rawMaterials;
    public IReadOnlyList<BomByProductLine> ByProducts => _byProducts;
    public IReadOnlyList<BomExpenseLine> Expenses => _expenses;

    private BillOfMaterials()
    {
    }

    public static BillOfMaterials Create(
        Guid organizationId, Guid productId, decimal outputQuantity, bool manufactureOnEverySale, string? notes)
    {
        EnsurePositiveOutputQuantity(outputQuantity);

        return new BillOfMaterials
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            OutputQuantity = outputQuantity,
            ManufactureOnEverySale = manufactureOnEverySale,
            Notes = notes,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(
        Guid productId, decimal outputQuantity, bool manufactureOnEverySale, string? notes, bool isActive)
    {
        EnsurePositiveOutputQuantity(outputQuantity);

        ProductId = productId;
        OutputQuantity = outputQuantity;
        ManufactureOnEverySale = manufactureOnEverySale;
        Notes = notes;
        IsActive = isActive;
    }

    public void AddRawMaterial(Guid productId, decimal quantity)
    {
        ProductionLineRules.EnsurePositiveQuantity(quantity, "A bill of materials raw-material line");
        _rawMaterials.Add(BomRawMaterialLine.Create(Id, productId, quantity));
    }

    public void AddByProduct(Guid productId, decimal costAllocationPct, decimal quantity)
    {
        ProductionLineRules.EnsurePositiveQuantity(quantity, "A bill of materials by-product line");
        ProductionLineRules.EnsureAllocationPercentageInRange(costAllocationPct);
        _byProducts.Add(BomByProductLine.Create(Id, productId, costAllocationPct, quantity));
    }

    public void AddExpense(Guid costTermId, decimal amount)
    {
        ProductionLineRules.EnsureNonNegativeAmount(amount, "A bill of materials expense line");
        _expenses.Add(BomExpenseLine.Create(Id, costTermId, amount));
    }

    /// <summary>Snapshotting callers (see the Create/Update handlers) remove the old children through
    /// their own DbSet before re-adding -- phase-4 bug #1's full-collection-replace remedy.</summary>
    public void ClearLines()
    {
        _rawMaterials.Clear();
        _byProducts.Clear();
        _expenses.Clear();
    }

    /// <summary>
    /// The one invariant a BOM enforces about its by-products, and the reason it is enforced here
    /// rather than in a validator: allocating 100% or more of the cost of production to
    /// by-products leaves the finished good entering stock at zero or negative cost, and a
    /// zero-cost FIFO layer silently makes every future sale of it 100% margin. Refusing with the
    /// real total named beats truncating (phase-24 Decision F's precedent).
    /// </summary>
    public void EnsureByProductAllocationIsSane()
    {
        ProductionLineRules.EnsureAllocationTotalUnderOneHundred(_byProducts.Sum(x => x.CostAllocationPct));
    }

    private static void EnsurePositiveOutputQuantity(decimal outputQuantity)
    {
        if (outputQuantity <= 0)
        {
            throw new InvalidOperationException("A bill of materials needs a positive Output Quantity.");
        }
    }
}
