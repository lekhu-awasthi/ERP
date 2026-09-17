namespace ErpApp.Application.Inventory;

/// <summary>Shared line-input shape for Create/Update WarehouseTransfer.</summary>
/// <param name="UnitId">Phase 52 -- the unit this line is entered in. Null means the product's
/// own primary unit, which is what every line written before this phase meant and what a client
/// that does not send the field means, so an unaware caller behaves exactly as before. A unit
/// that is neither the product's primary nor one of its secondary units is a 400 naming the
/// line. The conversion factor is <b>not</b> accepted from the client: it is resolved from the
/// catalogue once, by <c>DocumentLineUnitResolver</c>, and frozen on the line.</param>
public sealed record WarehouseTransferLineInput(Guid ProductId, decimal Quantity, Guid? UnitId = null);
