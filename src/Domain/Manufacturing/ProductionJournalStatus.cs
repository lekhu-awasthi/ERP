namespace ErpApp.Domain.Manufacturing;

/// <summary>
/// Draft -> Approved -> Void, live-confirmed: the list carries Approved/Draft tabs, an unapproved
/// journal shows "This transaction is still in DRAFT" with an Approve button, the document number
/// (PJ0008) is stamped at Approve, and the approved document's OPTION menu offers "Void this
/// Production Journal". No <c>Converted</c> member -- a Journal is the end of the chain.
/// </summary>
public enum ProductionJournalStatus
{
    Draft,
    Approved,
    Void,
}
