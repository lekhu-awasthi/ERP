namespace ErpApp.Domain.Workflow;

/// <summary>
/// What a WorkTask can attach to. Contact and Organization were the two live-confirmed parents in
/// Phase 13/18; Phase 27a adds the 15 transactional document types, because every transactional
/// detail page in the reference product carries a Tasks tab -- confirmed live on Invoice, Journal
/// Voucher and Warehouse Transfer, each showing the same DUE / DETAILS / PRIORITY / STATUS /
/// CREATED BY / ASSIGNED TO table with a "+ ADD TASK" action.
///
/// <para><b>The document members are named identically to their <see cref="ErpApp.Domain.Common.DocumentType"/>
/// counterparts, and that is load-bearing:</b> DocumentParentTypes maps between the two with
/// Enum.TryParse <i>by name</i>, never by ordinal (the phase-26a lesson -- an ordinal cast compiles,
/// works today, and silently reports the wrong type the first time a member is inserted).
/// DocumentMechanismSweepGuardTests asserts every transactional DocumentType has a counterpart here,
/// so a later phase cannot add a document type and quietly leave it Task-less.</para>
///
/// <para>Deliberately still not DocumentType itself: this enum has Contact and Organization, which
/// are not documents at all, and DocumentType has nine members that are numbering-pool stubs or
/// non-documents. Two overlapping-but-distinct vocabularies, bridged by name.</para>
/// </summary>
public enum TaskParentType
{
    Contact,
    Organization,

    // Phase 27a -- appended, so no persisted ordinal moves.
    Quotation,
    SalesOrder,
    Invoice,
    CreditNote,
    Payment,
    PurchaseOrder,
    PurchaseBill,
    Expense,
    DebitNote,
    JournalVoucher,
    CashTransfer,
    WarehouseTransfer,
    InventoryAdjustment,
    ProductionOrder,
    ProductionJournal,
}
