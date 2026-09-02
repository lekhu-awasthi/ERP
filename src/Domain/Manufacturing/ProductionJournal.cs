using ErpApp.Domain.Common;

namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// The costed execution of a production run (FR-8.9, erp-module-scan.md Inventory §10) -- the
/// first document in this system that <b>transforms</b> value rather than moving it. It consumes
/// several stock items at their real FIFO cost, adds non-stock expense, and creates a different
/// stock item at a cost it computes itself. That computed cost becomes a FIFO layer, so every
/// future Invoice's COGS reads from it.
///
/// <para><b>The invariant, which is what the tests are built around:</b></para>
/// <code>
/// raw-material FIFO cost consumed  +  production expenses
///         =  finished-goods stock value created  +  by-product stock value created
///            ( + CostRoundingAdjustment, see below)
/// </code>
///
/// <para>Value in equals value out. <see cref="ComputeAndRecordRollUp"/> is where that is made
/// true, and it is deliberately in the Domain (not the handler) so it can be proven without a
/// database. The handler's job is only to supply the one thing Domain cannot know -- what
/// ConsumeAsync actually returned per raw line -- and then to create the stock layers and the GL
/// entry from the figures recorded here.</para>
///
/// <para><b>Why a rounding adjustment exists at all.</b> A FIFO layer stores a unit cost, not a
/// value, so the value it represents is <c>Quantity * Round(cost, 4)</c>. Whenever the finished
/// goods cost does not divide evenly by the output quantity at four decimals, the layer is worth a
/// fraction of a cent less (or more) than the cost that went into it. That residue is real,
/// bounded by <c>OutputQuantity * 0.00005</c>, and is <b>named rather than hidden</b>: the GL is
/// posted from the values actually created so it balances by construction, and
/// <see cref="CostRoundingAdjustment"/> reports the difference. It is zero for every ordinary
/// whole-quantity run. The reference product has the same residue and simply does not show it --
/// its own PJ0006 rolled 3250 into 240 units at a displayed 13.54, which multiplies back to
/// 3249.60.</para>
/// </summary>
public sealed class ProductionJournal
{
    public const string DraftCode = "DRAFT";

    /// <summary>Matches StockLedgerEntry.UnitCost's own scale exactly. A unit cost computed here at
    /// any other precision would disagree with the layer it creates the moment it is persisted,
    /// which is the one divergence this whole aggregate exists to prevent.</summary>
    public const int UnitCostScale = 4;

    private readonly List<ProductionJournalRawMaterialLine> _rawMaterials = [];
    private readonly List<ProductionJournalByProductLine> _byProducts = [];
    private readonly List<ProductionJournalExpenseLine> _expenses = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal OutputQuantity { get; private set; }

    /// <summary>Required, unlike the Production Order's -- live-confirmed, and necessary here
    /// regardless: every FIFO layer in this codebase is keyed (ProductId, WarehouseId), so a
    /// document that both consumes and creates stock has to say where.</summary>
    public Guid WarehouseId { get; private set; }

    public Guid? BillOfMaterialsId { get; private set; }
    public string? Notes { get; private set; }
    public ProductionJournalStatus Status { get; private set; }

    /// <summary>Set when this journal was created from a Production Order. Enforces nothing on its
    /// own (phase-6 bug #4) -- ProductionOrder.MarkConverted is the gate.</summary>
    public DocumentType? ReferrerType { get; private set; }

    public Guid? ReferrerId { get; private set; }

    public decimal? RawMaterialCost { get; private set; }
    public decimal? ProductionExpenseCost { get; private set; }
    public decimal? CostAllocatedToByProduct { get; private set; }
    public decimal? FinishedGoodsCost { get; private set; }
    public decimal? FinishedGoodsUnitCost { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    public IReadOnlyList<ProductionJournalRawMaterialLine> RawMaterials => _rawMaterials;
    public IReadOnlyList<ProductionJournalByProductLine> ByProducts => _byProducts;
    public IReadOnlyList<ProductionJournalExpenseLine> Expenses => _expenses;

    /// <summary>Derived, never stored: it is the sum of two figures that <i>are</i> stored, so
    /// there is nothing here that could drift out of step with them. Matches the reference
    /// product's own roll-up line "Total Cost of Production".</summary>
    public decimal? TotalCostOfProduction =>
        RawMaterialCost is { } raw && ProductionExpenseCost is { } expenses ? raw + expenses : null;

    /// <summary>See the class remarks. Zero for any run whose finished cost divides evenly at four
    /// decimals, which is the overwhelmingly common case.</summary>
    public decimal? CostRoundingAdjustment =>
        TotalCostOfProduction is { } total && CostAllocatedToByProduct is { } byProduct && FinishedGoodsCost is { } finished
            ? total - byProduct - finished
            : null;

    private ProductionJournal()
    {
    }

    public static ProductionJournal Create(
        Guid organizationId,
        DateOnly date,
        string? reference,
        Guid productId,
        decimal outputQuantity,
        Guid warehouseId,
        Guid? billOfMaterialsId,
        string? notes,
        DocumentType? referrerType,
        Guid? referrerId)
    {
        EnsurePositiveOutputQuantity(outputQuantity);

        return new ProductionJournal
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = DraftCode,
            Date = date,
            Reference = reference,
            ProductId = productId,
            OutputQuantity = outputQuantity,
            WarehouseId = warehouseId,
            BillOfMaterialsId = billOfMaterialsId,
            Notes = notes,
            Status = ProductionJournalStatus.Draft,
            ReferrerType = referrerType,
            ReferrerId = referrerId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateHeader(
        DateOnly date,
        string? reference,
        Guid productId,
        decimal outputQuantity,
        Guid warehouseId,
        Guid? billOfMaterialsId,
        string? notes)
    {
        EnsureDraft();
        EnsurePositiveOutputQuantity(outputQuantity);

        Date = date;
        Reference = reference;
        ProductId = productId;
        OutputQuantity = outputQuantity;
        WarehouseId = warehouseId;
        BillOfMaterialsId = billOfMaterialsId;
        Notes = notes;
    }

    public void AddRawMaterial(Guid productId, decimal quantity)
    {
        EnsureDraft();
        ProductionLineRules.EnsurePositiveQuantity(quantity, "A production journal raw-material line");
        _rawMaterials.Add(ProductionJournalRawMaterialLine.Create(Id, productId, quantity));
    }

    public void AddByProduct(Guid productId, decimal costAllocationPct, decimal quantity)
    {
        EnsureDraft();
        ProductionLineRules.EnsurePositiveQuantity(quantity, "A production journal by-product line");
        ProductionLineRules.EnsureAllocationPercentageInRange(costAllocationPct);
        _byProducts.Add(ProductionJournalByProductLine.Create(Id, productId, costAllocationPct, quantity));
    }

    public void AddExpense(Guid costTermId, decimal amount)
    {
        EnsureDraft();
        ProductionLineRules.EnsureNonNegativeAmount(amount, "A production journal expense line");
        _expenses.Add(ProductionJournalExpenseLine.Create(Id, costTermId, amount));
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

    /// <summary>
    /// The cost roll-up, run once at Approve after every raw-material line has been stamped with
    /// what ConsumeAsync actually returned. Order matters and is the whole of Decision C:
    /// by-products are allocated a slice of the Total Cost of Production <i>first</i>, and the
    /// finished goods get what is left -- allocating cost to a by-product without subtracting it
    /// from the finished good is exactly how a production journal creates value from nothing.
    /// </summary>
    public void ComputeAndRecordRollUp()
    {
        if (_rawMaterials.Count == 0 || _rawMaterials.Any(x => x.Amount is null))
        {
            throw new InvalidOperationException(
                "Every raw-material line must have its consumed FIFO cost recorded before the cost roll-up can be computed.");
        }

        var rawMaterialCost = _rawMaterials.Sum(x => x.Amount!.Value);
        var productionExpenseCost = _expenses.Sum(x => x.Amount);
        var totalCostOfProduction = rawMaterialCost + productionExpenseCost;

        var allocatedToByProduct = 0m;
        foreach (var byProduct in _byProducts)
        {
            var allocation = totalCostOfProduction * byProduct.CostAllocationPct / 100m;
            var unitCost = Math.Round(allocation / byProduct.Quantity, UnitCostScale, MidpointRounding.AwayFromZero);

            // The layer's real worth, not the theoretical allocation -- everything downstream (the
            // GL entry, the conservation assertion) has to use what stock is actually carrying.
            var actualValue = unitCost * byProduct.Quantity;

            byProduct.RecordAllocatedCost(unitCost, actualValue);
            allocatedToByProduct += actualValue;
        }

        var finishedGoodsTarget = totalCostOfProduction - allocatedToByProduct;
        var finishedGoodsUnitCost = Math.Round(
            finishedGoodsTarget / OutputQuantity, UnitCostScale, MidpointRounding.AwayFromZero);

        RawMaterialCost = rawMaterialCost;
        ProductionExpenseCost = productionExpenseCost;
        CostAllocatedToByProduct = allocatedToByProduct;
        FinishedGoodsUnitCost = finishedGoodsUnitCost;
        FinishedGoodsCost = finishedGoodsUnitCost * OutputQuantity;
    }

    public void Approve(Guid approvedByUserId, string code)
    {
        EnsureDraft();

        if (_rawMaterials.Count == 0)
        {
            throw new InvalidOperationException("A production journal needs at least one raw material to be approved.");
        }

        Status = ProductionJournalStatus.Approved;
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = DateTimeOffset.UtcNow;
        Code = code;
    }

    public void Void(Guid voidedByUserId)
    {
        if (Status != ProductionJournalStatus.Approved)
        {
            throw new InvalidOperationException("Only an Approved production journal can be voided.");
        }

        Status = ProductionJournalStatus.Void;
        VoidedByUserId = voidedByUserId;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    private static void EnsurePositiveOutputQuantity(decimal outputQuantity)
    {
        if (outputQuantity <= 0)
        {
            throw new InvalidOperationException("A production journal needs a positive Output Quantity.");
        }
    }

    private void EnsureDraft()
    {
        if (Status != ProductionJournalStatus.Draft)
        {
            throw new InvalidOperationException("This production journal is no longer in Draft status.");
        }
    }
}
