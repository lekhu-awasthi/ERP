namespace ErpApp.Domain.Common;

/// <summary>
/// Shared cross-bounded-context vocabulary (architecture-spec.md §3.1/§3.6) -- not owned by any
/// one bounded context, since CustomStatus/DocumentNumberingRule/CustomFieldDefinition (Phase 2,
/// Configuration context) all reference it, and later phases' real aggregates (Invoice,
/// PurchaseBill, JournalVoucher, ...) will too. The 17 entries mirror architecture-spec.md §3.6's
/// "17 document types" count and every document type named across §4, including the
/// numbering-pool-only codes (Account, Contact, Product -- see §3.1's "Separate numbering pools
/// for Account codes and Contact/Item codes reuse the same service").
/// </summary>
public enum DocumentType
{
    Quotation,
    SalesOrder,
    Invoice,
    CreditNote,
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
    Account,
    Contact,
    Product,

    /// <summary>Added Phase 5 (Sales chain) -- Customer Payment's own numbering pool. Supplier
    /// Payment (Phase 6, Direction=Paid) reuses the same Payment aggregate and this same pool.</summary>
    Payment,
}
