using ErpApp.Domain.Common;

namespace ErpApp.Domain.Inventory;

/// <summary>
/// Child line of InventoryAdjustment. UnitCost is required and meaningful only for a Direction=
/// Increase line (there's no existing FIFO layer to derive a cost from -- the user states what the
/// found/corrected-in stock is worth, same as a PurchaseBill line's Rate). A Direction=Decrease
/// line carries UnitCost=0; its real cost is resolved at Approve time from whichever existing FIFO
/// layers IStockLedgerService.Consume actually walks -- storing a user-entered cost on a Decrease
/// line would let it diverge from the FIFO layers it's actually consuming, corrupting the ledger.
/// </summary>
public sealed class InventoryAdjustmentLine
{
    public Guid Id { get; private set; }
    public Guid InventoryAdjustmentId { get; private set; }
    public Guid ProductId { get; private set; }
    public InventoryAdjustmentDirection Direction { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }

    /// <summary>Phase 54 -- the unit this line was <b>entered</b> in (null means the product's own
    /// primary unit) and how many primary units one of them is worth, frozen when the line was
    /// written. See <c>InvoiceLine</c> for the full reasoning and <see cref="UnitConversion"/> for
    /// the live evidence behind freezing the factor rather than re-reading it.
    ///
    /// <para>Phase 52 deferred this type <b>unread</b>, because the reference tenant held no
    /// inventory adjustments. The 2026-09-20 row-present read settled it: with one line on the grid
    /// the vendor's Qty cell renders the unit control, and for a product carrying secondary units it
    /// is a real select listing the whole matrix -- markup identical, attribute for attribute, to
    /// the Invoice grid's. So this is the same mechanism as the other eight, not a new one. The
    /// three manufacturing types read in the same pass are <b>not</b>: see
    /// <c>UnitSweepGuardTests.UnitlessLineTypes</c>.</para></summary>
    public Guid? UnitId { get; private set; }

    /// <inheritdoc cref="UnitId"/>
    public decimal ConversionFactor { get; private set; }

    /// <summary>This line's quantity in the product's primary unit -- the only quantity the stock
    /// ledger and the GL accept. Derived, never stored: a column would be a second quantity able to
    /// contradict <see cref="Quantity"/> and <see cref="ConversionFactor"/> (phase 51's
    /// <c>ProductBatch</c> argument, phase 37's two-of-three-views failure).</summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    /// <summary>Null until ApproveInventoryAdjustmentCommandHandler actually consumes FIFO stock
    /// for a Direction=Decrease line (an Increase line never gets one -- its cost is the
    /// user-entered UnitCost above). Set once, from IStockLedgerService.ConsumeAsync's actual
    /// weighted-average result -- mirrors InvoiceLine.CogsUnitCost's precedent -- so voiding this
    /// adjustment can put stock back at the exact cost it left at.</summary>
    public decimal? ConsumedUnitCost { get; private set; }

    private InventoryAdjustmentLine()
    {
    }

    internal static InventoryAdjustmentLine Create(
        Guid inventoryAdjustmentId,
        Guid productId,
        InventoryAdjustmentDirection direction,
        decimal quantity,
        decimal unitCost,
        Guid? unitId,
        decimal conversionFactor)
    {
        return new InventoryAdjustmentLine
        {
            Id = Guid.NewGuid(),
            InventoryAdjustmentId = inventoryAdjustmentId,
            ProductId = productId,
            Direction = direction,
            UnitId = unitId,
            ConversionFactor = UnitConversion.Validate(conversionFactor),
            Quantity = quantity,
            UnitCost = direction == InventoryAdjustmentDirection.Increase ? unitCost : 0,
        };
    }

    /// <summary>Called once, from ApproveInventoryAdjustmentCommandHandler right after
    /// IStockLedgerService.ConsumeAsync returns this Decrease line's actual weighted-average cost.
    /// Public (not internal) for the same Domain/Application assembly-boundary reason InvoiceLine.
    /// RecordCogsUnitCost is public.</summary>
    public void RecordConsumedUnitCost(decimal unitCost) => ConsumedUnitCost = unitCost;
}
