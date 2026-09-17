using ErpApp.Domain.Common;

namespace ErpApp.Domain.Inventory;

/// <summary>Child line of WarehouseTransfer -- one row per Product moved, created only via
/// WarehouseTransfer.AddLine.</summary>
public sealed class WarehouseTransferLine
{
    public Guid Id { get; private set; }
    public Guid WarehouseTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Phase 52 -- the unit this line was <b>entered</b> in (null means the product's own
    /// primary unit) and how many primary units one of them is worth, frozen when the line was
    /// written. See <c>InvoiceLine</c> for the full reasoning and <see cref="UnitConversion"/> for
    /// the live evidence behind freezing the factor rather than re-reading it.
    ///
    /// <para>A Warehouse Transfer is the one line type carrying a unit that carries no <b>price</b>,
    /// which is why the rule this phase followed is <i>every line naming a product and a
    /// quantity</i> rather than <i>every priced line</i> -- a list sampled from the sales and
    /// purchase forms would have missed it (phase 30). Confirmed live: the vendor's
    /// <c>warehouse-transfers</c> line carries <c>measurement_unit_id</c> like the other seven.</para></summary>
    public Guid? UnitId { get; private set; }

    /// <inheritdoc cref="UnitId"/>
    public decimal ConversionFactor { get; private set; }

    /// <summary>This line's quantity in the product's primary unit -- the only quantity the stock
    /// ledger and the GL accept.</summary>
    public PrimaryQuantity PrimaryQuantity => PrimaryQuantity.FromEntered(Quantity, ConversionFactor);

    private WarehouseTransferLine()
    {
    }

    internal static WarehouseTransferLine Create(
        Guid warehouseTransferId, Guid productId, decimal quantity, Guid? unitId, decimal conversionFactor)
    {
        return new WarehouseTransferLine
        {
            Id = Guid.NewGuid(),
            WarehouseTransferId = warehouseTransferId,
            ProductId = productId,
            UnitId = unitId,
            ConversionFactor = UnitConversion.Validate(conversionFactor),
            Quantity = quantity,
        };
    }
}
