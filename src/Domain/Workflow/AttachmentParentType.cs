namespace ErpApp.Domain.Workflow;

/// <summary>
/// What an Attachment can hang off. Phase 18 deliberately kept this separate from TaskParentType
/// (decision #2) even though the (ParentType, ParentId) shape is identical, because the reference
/// product's Contact "Documents" tab and its Workflow "Document" inbox are functionally different
/// screens -- see git history for the full argument. That reasoning is unchanged: this enum still
/// describes plain file attachments, and Phase 22's UploadedDocument still lives apart.
///
/// <para>Phase 27a adds the 15 transactional document types: every transactional detail page has a
/// Documents tab, and it is the plain drag-and-drop dropzone, not the extraction inbox -- so it is
/// the same concept as the Contact tab, and reusing this enum is the confirmed-correct call rather
/// than an assumed one. Members are named identically to their
/// <see cref="ErpApp.Domain.Common.DocumentType"/> counterparts; see TaskParentType for why that
/// matters and which test enforces it.</para>
/// </summary>
public enum AttachmentParentType
{
    Contact,

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
