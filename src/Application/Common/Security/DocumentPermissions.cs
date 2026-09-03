using ErpApp.Domain.Common;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 27a -- the one place a <see cref="DocumentType"/> becomes a permission key.
///
/// <para>Before this, three near-identical switch statements
/// (<c>CustomFieldValuePermissions</c>, <c>TransactionReportingTagPermissions</c>,
/// <c>CustomStatusPermissions</c>) each mapped the same two document types to the same two keys,
/// and each carried its own copy of the "not wired up yet" throw. Sweeping all four mechanisms
/// across every document type would have made that three parallel 17-arm switches that must agree
/// -- three chances to drift. They now all delegate here, so a mechanism's applicability list
/// (<see cref="DocumentMechanisms"/>) and the key lookup are separate concerns with one
/// implementation each. Same reasoning as phase-26b's shared readers: the reports agree because
/// they read the same code, not because someone checked.</para>
///
/// <para>No new <c>PermissionKeys</c> constants: attaching a custom field, a reporting tag, a custom
/// status, a task, a file or a comment to a document is an edit of that document, gated by that
/// document's own Edit key -- the decision Phase 19 made and 20a/20b reaffirmed, now applied
/// uniformly. Reading any of them rides the document's View key.</para>
/// </summary>
public static class DocumentPermissions
{
    /// <summary>
    /// The key that lets a caller change something attached to this document type.
    ///
    /// <para>OpeningBalance and OpeningStock both map to <c>OpeningBalanceEdit</c>: they are the two
    /// tabs of one Opening Balances screen live, and <c>CreateOrUpdateOpeningStockLineCommand</c>
    /// already rides that key rather than one of its own.</para>
    /// </summary>
    public static string EditPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationEdit,
        DocumentType.SalesOrder => PermissionKeys.SalesOrderEdit,
        DocumentType.Invoice => PermissionKeys.InvoiceEdit,
        DocumentType.CreditNote => PermissionKeys.CreditNoteEdit,
        DocumentType.Payment => PermissionKeys.PaymentEdit,
        DocumentType.PurchaseOrder => PermissionKeys.PurchaseOrderEdit,
        DocumentType.PurchaseBill => PermissionKeys.PurchaseBillEdit,
        DocumentType.Expense => PermissionKeys.ExpenseEdit,
        DocumentType.DebitNote => PermissionKeys.DebitNoteEdit,
        DocumentType.JournalVoucher => PermissionKeys.JournalVoucherEdit,
        DocumentType.CashTransfer => PermissionKeys.CashTransferEdit,
        DocumentType.WarehouseTransfer => PermissionKeys.WarehouseTransferEdit,
        DocumentType.InventoryAdjustment => PermissionKeys.InventoryAdjustmentEdit,
        DocumentType.ProductionOrder => PermissionKeys.ProductionOrderEdit,
        DocumentType.ProductionJournal => PermissionKeys.ProductionJournalEdit,
        DocumentType.OpeningBalance or DocumentType.OpeningStock => PermissionKeys.OpeningBalanceEdit,
        _ => throw NotADocument(documentType),
    };

    /// <summary>The key that lets a caller read something attached to this document type.</summary>
    public static string ViewPermissionFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationView,
        DocumentType.SalesOrder => PermissionKeys.SalesOrderView,
        DocumentType.Invoice => PermissionKeys.InvoiceView,
        DocumentType.CreditNote => PermissionKeys.CreditNoteView,
        DocumentType.Payment => PermissionKeys.PaymentView,
        DocumentType.PurchaseOrder => PermissionKeys.PurchaseOrderView,
        DocumentType.PurchaseBill => PermissionKeys.PurchaseBillView,
        DocumentType.Expense => PermissionKeys.ExpenseView,
        DocumentType.DebitNote => PermissionKeys.DebitNoteView,
        DocumentType.JournalVoucher => PermissionKeys.JournalVoucherView,
        DocumentType.CashTransfer => PermissionKeys.CashTransferView,
        DocumentType.WarehouseTransfer => PermissionKeys.WarehouseTransferView,
        DocumentType.InventoryAdjustment => PermissionKeys.InventoryAdjustmentView,
        DocumentType.ProductionOrder => PermissionKeys.ProductionOrderView,
        DocumentType.ProductionJournal => PermissionKeys.ProductionJournalView,
        DocumentType.OpeningBalance or DocumentType.OpeningStock => PermissionKeys.OpeningBalanceView,
        _ => throw NotADocument(documentType),
    };

    private static ArgumentOutOfRangeException NotADocument(DocumentType documentType) =>
        new(
            nameof(documentType),
            documentType,
            $"{documentType} is not a document anything can be attached to -- see "
                + "DocumentMechanisms.NotApplicableReasons for why.");
}
