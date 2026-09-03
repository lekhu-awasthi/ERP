namespace ErpApp.Domain.Workflow;

/// <summary>
/// What a Comment can hang off. Phase 18 decision #3 gave Comment a fixed ContactId FK rather than
/// a polymorphic pair, on the explicit condition that it would "generalize to polymorphic only
/// if/when a second parent type is actually needed."
///
/// <para>Phase 27a is when: every transactional detail page's Activity tab opens with a real comment
/// composer ("Write comment here...", ADD COMMENT) above sub-tabs Comments / Activities / Emails --
/// confirmed live on Invoice, Journal Voucher and Warehouse Transfer. So a comment on a document is
/// the same concept as a comment on a Contact, and the deferred generalisation happens now, on
/// evidence, exactly as that decision anticipated.</para>
///
/// <para>Contact is first so the migration can map every existing row to it. Members are named
/// identically to their <see cref="ErpApp.Domain.Common.DocumentType"/> counterparts; see
/// TaskParentType for why that matters.</para>
/// </summary>
public enum CommentParentType
{
    Contact,

    // Phase 27a -- the 15 transactional document types.
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
