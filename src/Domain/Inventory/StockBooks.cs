using ErpApp.Domain.Common;

namespace ErpApp.Domain.Inventory;

/// <summary>
/// Phase 58 -- which of the two stock ledgers each <see cref="DocumentType"/> writes. The single
/// source of truth for that, and the thing <c>StockBookSweepGuardTests</c> reads.
///
/// <para><b>There are two ledgers, and there always are.</b> The reference product's "Mode of
/// Inventory Tracking" setting reads as though it moved stock consumption from the Invoice to the
/// Delivery Note. It does not (read and written live on 2026-09-24, docs/erp-module-scan.md): a GRN
/// wrote the <i>physical</i> ledger and left the accounting one untouched, the Purchase Bill wrote the
/// <i>accounting</i> ledger and left the physical one untouched, and an Inventory Adjustment wrote
/// both. The setting chooses which ledger the inventory screens read by default -- it moves
/// nothing.</para>
///
/// <para><b>The accounting ledger is the one this codebase already has.</b> <c>StockLedgerEntry</c>
/// and <c>StockMovement</c>, FIFO-costed, tied to the Inventory account by phase 37's conservation
/// law. Nothing about it changes in either mode, which is why that law holds in both by
/// construction rather than by a second proof.</para>
///
/// <para><b>The physical ledger is quantity-only, and mostly derived.</b> Its balance is the
/// <see cref="PhysicalStockMovement"/> rows the two <see cref="PhysicalOnly"/> documents write,
/// <i>plus</i> the <c>StockMovement</c> rows of the <see cref="Shared"/> documents -- read, not
/// copied. Deriving rather than dual-writing means no existing stock writer changed, no history had
/// to be backfilled (an adjustment approved in phase 7 already counts), and no reader of the
/// accounting tables can ever see a physical row, because there is none in those tables.
/// It carries no value: it posts nothing, so a second FIFO valuation would have no account to agree
/// with (docs/phase-58-status.md, Decision A).</para>
///
/// <para>Every member of the enum is in exactly one of the three lists or in
/// <see cref="NotStockMovingReasons"/> -- the <c>DocumentMechanisms</c> idiom, so a later phase
/// adding a document type cannot forget to decide which ledger it belongs to.</para>
/// </summary>
public static class StockBooks
{
    /// <summary>
    /// Commercial documents: they move the accounting ledger only. Measured live for Purchase Bill,
    /// Invoice and Credit Note; Debit Note by inference as the Credit Note's mirror.
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> AccountingOnly =
    [
        DocumentType.Invoice,
        DocumentType.CreditNote,
        DocumentType.PurchaseBill,
        DocumentType.DebitNote,
    ];

    /// <summary>The physical-movement documents: they move the physical ledger only.</summary>
    public static readonly IReadOnlyList<DocumentType> PhysicalOnly =
    [
        DocumentType.DeliveryNote,
        DocumentType.GoodsReceivedNote,
    ];

    /// <summary>
    /// Stock documents that are neither a sale nor a purchase: goods really move <i>and</i> the
    /// books really change, so both ledgers count them. Measured live for Inventory Adjustment;
    /// the other three by inference (the reference tenant has one warehouse and no manufacturing).
    /// </summary>
    public static readonly IReadOnlyList<DocumentType> Shared =
    [
        DocumentType.WarehouseTransfer,
        DocumentType.InventoryAdjustment,
        DocumentType.ProductionJournal,
        DocumentType.OpeningStock,
    ];

    /// <summary>The types whose movements the physical ledger counts.</summary>
    public static readonly IReadOnlyList<DocumentType> Physical = [.. PhysicalOnly, .. Shared];

    /// <summary>Every <see cref="DocumentType"/> that writes no stock at all, with the reason.</summary>
    public static readonly IReadOnlyDictionary<DocumentType, string> NotStockMovingReasons =
        new Dictionary<DocumentType, string>
        {
            [DocumentType.Quotation] = "An offer; nothing has moved.",
            [DocumentType.SalesOrder] = "An order; the goods move on its Delivery Note or Invoice.",
            [DocumentType.PurchaseOrder] = "An order; the goods move on its GRN or Purchase Bill.",
            [DocumentType.Expense] = "Service spending; Expense carries no product lines.",
            [DocumentType.JournalVoucher] = "Posts to accounts, never to stock.",
            [DocumentType.CashTransfer] = "Moves money between accounts.",
            [DocumentType.ProductionOrder] = "A plan; the Production Journal is what moves stock.",
            [DocumentType.Account] = "A numbering pool, not a document.",
            [DocumentType.Contact] = "A numbering pool, not a document.",
            [DocumentType.Product] = "A numbering pool, not a document.",
            [DocumentType.Payment] = "Settles money; no goods.",
            [DocumentType.OpeningBalance] = "An account's opening balance; stock opens through OpeningStock.",
            [DocumentType.DataExport] = "An audit marker, not a document.",
            [DocumentType.MigratedSalesEntry] = "A migrated register row; it never touched this ledger.",
            [DocumentType.MigratedPurchaseEntry] = "A migrated register row; it never touched this ledger.",
            [DocumentType.DocumentExtraction] = "An audit marker, not a document.",
            [DocumentType.Deal] = "A CRM record.",
            [DocumentType.WorkTask] = "A CRM record.",
        };
}
