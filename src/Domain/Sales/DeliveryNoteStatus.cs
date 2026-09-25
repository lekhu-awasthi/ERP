namespace ErpApp.Domain.Sales;

/// <summary>Phase 58 -- Draft -> Approved -> Void. No Converted state: the reference product has no
/// Delivery Note -> Invoice action (its invoice's reference picker does not list delivery notes).</summary>
public enum DeliveryNoteStatus
{
    Draft,
    Approved,
    Void,
}
