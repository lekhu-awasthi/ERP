namespace ErpApp.Application.Inventory;

/// <summary>Shared line-input shape for Create/Update WarehouseTransfer.</summary>
public sealed record WarehouseTransferLineInput(Guid ProductId, decimal Quantity);
