namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// The uncosted production plan (FR-8.8, erp-module-scan.md Inventory §9), and the conversion
/// source for <see cref="ProductionJournal"/> -- the fourth conversion in this codebase after
/// Quotation -> Invoice, SalesOrder -> Invoice and PurchaseOrder -> PurchaseBill, and built with
/// phase-6 bug #4's enforcement applied from day one rather than retrofitted.
///
/// <para><b>Nothing here touches stock, the general ledger or COGS.</b> Approve() assigns the
/// document number and flips the status; that is the whole of its side effects. The plan carries
/// no warehouse (live-confirmed: the Production Order form has no Warehouse field, while the
/// Journal's is required) because nothing is moving yet.</para>
/// </summary>
public sealed class ProductionOrder
{
    public const string DraftCode = "DRAFT";

    private readonly List<ProductionOrderRawMaterialLine> _rawMaterials = [];
    private readonly List<ProductionOrderByProductLine> _byProducts = [];
    private readonly List<ProductionOrderExpenseLine> _expenses = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal OutputQuantity { get; private set; }

    /// <summary>Nullable, and stays null when the planner typed the lines by hand. Recorded when a
    /// BOM was loaded so the Production Variance report has a plan to compare the eventual Journal
    /// against -- live-confirmed that only journals with a BOM appear in that report at all.</summary>
    public Guid? BillOfMaterialsId { get; private set; }

    public string? Notes { get; private set; }
    public ProductionOrderStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<ProductionOrderRawMaterialLine> RawMaterials => _rawMaterials;
    public IReadOnlyList<ProductionOrderByProductLine> ByProducts => _byProducts;
    public IReadOnlyList<ProductionOrderExpenseLine> Expenses => _expenses;

    private ProductionOrder()
    {
    }

    public static ProductionOrder Create(
        Guid organizationId,
        DateOnly date,
        string? reference,
        Guid productId,
        decimal outputQuantity,
        Guid? billOfMaterialsId,
        string? notes)
    {
        if (outputQuantity <= 0)
        {
            throw new InvalidOperationException("A production order needs a positive Output Quantity.");
        }

        return new ProductionOrder
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            ProductId = productId,
            OutputQuantity = outputQuantity,
            BillOfMaterialsId = billOfMaterialsId,
            Notes = notes,
            Status = ProductionOrderStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(
        DateOnly date, string? reference, Guid productId, decimal outputQuantity, Guid? billOfMaterialsId, string? notes)
    {
        EnsureDraft();

        if (outputQuantity <= 0)
        {
            throw new InvalidOperationException("A production order needs a positive Output Quantity.");
        }

        Date = date;
        Reference = reference;
        ProductId = productId;
        OutputQuantity = outputQuantity;
        BillOfMaterialsId = billOfMaterialsId;
        Notes = notes;
    }

    public void AddRawMaterial(Guid productId, decimal quantity)
    {
        EnsureDraft();
        ProductionLineRules.EnsurePositiveQuantity(quantity, "A production order raw-material line");
        _rawMaterials.Add(ProductionOrderRawMaterialLine.Create(Id, productId, quantity));
    }

    public void AddByProduct(Guid productId, decimal costAllocationPct, decimal quantity)
    {
        EnsureDraft();
        ProductionLineRules.EnsurePositiveQuantity(quantity, "A production order by-product line");
        ProductionLineRules.EnsureAllocationPercentageInRange(costAllocationPct);
        _byProducts.Add(ProductionOrderByProductLine.Create(Id, productId, costAllocationPct, quantity));
    }

    public void AddExpense(Guid costTermId, decimal amount)
    {
        EnsureDraft();
        ProductionLineRules.EnsureNonNegativeAmount(amount, "A production order expense line");
        _expenses.Add(ProductionOrderExpenseLine.Create(Id, costTermId, amount));
    }

    public void ClearLines()
    {
        EnsureDraft();
        _rawMaterials.Clear();
        _byProducts.Clear();
        _expenses.Clear();
    }

    public void EnsureByProductAllocationIsSane() =>
        ProductionLineRules.EnsureAllocationTotalUnderOneHundred(_byProducts.Sum(x => x.CostAllocationPct));

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_rawMaterials.Count == 0)
        {
            throw new InvalidOperationException("A production order needs at least one raw material to be approved.");
        }

        Status = ProductionOrderStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    /// <summary>
    /// The single-conversion gate, and the reason <see cref="ProductionOrderStatus.Converted"/>
    /// exists at all: phase-6 bug #4 established that setting ReferrerType/ReferrerId on the target
    /// enforces nothing by itself. Called from CreateProductionJournalCommandHandler when the
    /// request names this order as its referrer; a second attempt lands here with Status already
    /// Converted and is refused.
    /// </summary>
    public void MarkConverted()
    {
        if (Status != ProductionOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved production order can be converted to a Production Journal.");
        }

        Status = ProductionOrderStatus.Converted;
    }

    /// <summary>Mirror of PurchaseOrder.Void -- a Converted order (live dependent: the Journal
    /// created from it) is rejected by the plain Approved-only status check.</summary>
    public void Void(Guid voidedByUserId)
    {
        if (Status != ProductionOrderStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved production order can be voided.");
        }

        Status = ProductionOrderStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != ProductionOrderStatus.Draft)
        {
            throw new InvalidOperationException("This production order is no longer in Draft status.");
        }
    }
}
