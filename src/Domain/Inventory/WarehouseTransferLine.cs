namespace ErpApp.Domain.Inventory;

/// <summary>Child line of WarehouseTransfer -- one row per Product moved, created only via
/// WarehouseTransfer.AddLine.</summary>
public sealed class WarehouseTransferLine
{
    public Guid Id { get; private set; }
    public Guid WarehouseTransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }

    private WarehouseTransferLine()
    {
    }

    internal static WarehouseTransferLine Create(Guid warehouseTransferId, Guid productId, decimal quantity)
    {
        return new WarehouseTransferLine
        {
            Id = Guid.NewGuid(),
            WarehouseTransferId = warehouseTransferId,
            ProductId = productId,
            Quantity = quantity,
        };
    }
}
