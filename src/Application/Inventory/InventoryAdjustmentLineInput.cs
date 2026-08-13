using ErpApp.Domain.Inventory;

namespace ErpApp.Application.Inventory;

/// <summary>Shared line-input shape for Create/Update InventoryAdjustment. UnitCost is required
/// only when Direction is Increase -- see InventoryAdjustmentLine's doc comment.</summary>
public sealed record InventoryAdjustmentLineInput(
    Guid ProductId, InventoryAdjustmentDirection Direction, decimal Quantity, decimal UnitCost);
