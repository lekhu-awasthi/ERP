namespace ErpApp.Domain.Purchasing;

/// <summary>Phase 58 -- Draft -> Approved -> Void, the plain transactional lifecycle. There is no
/// Converted state: nothing is converted <i>from</i> a GRN (the reference product has no GRN ->
/// Purchase Bill action; the bill references the Purchase Order instead).</summary>
public enum GoodsReceivedNoteStatus
{
    Draft,
    Approved,
    Void,
}
