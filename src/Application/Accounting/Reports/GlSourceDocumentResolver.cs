using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 26a -- resolves a set of posted <c>GlJournalEntry</c> rows back to the Txn No and
/// Reference No their source documents carry, which all three of this phase's line-level reports
/// (Journal report, Detail General Ledger, GL Master Report) show on every row.
///
/// <para><b>Why a resolver exists at all.</b> <c>GlJournalEntry</c> stores only
/// SourceDocumentType, SourceDocumentId and PostedAt -- it deliberately carries no copy of the
/// document's number or reference, because those belong to the document. Every report that shows
/// them therefore has to join back, and there are eleven document types that post GL
/// (grep-confirmed against every <c>GlJournalEntry.Post</c> call site: Invoice, CreditNote,
/// PurchaseBill, Expense, DebitNote, JournalVoucher, CashTransfer, InventoryAdjustment, Payment,
/// ProductionJournal, OpeningBalance -- Quotation, SalesOrder, PurchaseOrder and WarehouseTransfer
/// post nothing). Writing that join out once here beats writing it three times.</para>
///
/// <para><b>One batched round trip per type, not one per row.</b> Each type is its own concrete
/// <c>Where(ids.Contains(...))</c> -- not a generic helper parameterised by a <c>Func</c>, for the
/// usual translation reason (phase-9 bug #1) -- and is skipped entirely when the page contains no
/// row of that type.</para>
///
/// <para><b>Opening Balance is the one type with nothing to resolve.</b> Its SourceDocumentId is an
/// <c>OpeningBalanceLine</c>, which is keyed by (OrganizationId, AccountId) and has no code, no
/// reference and no business date -- it is not a numbered document. It reports the literal label
/// "Opening Balance" and a null reference, which is what the live product shows for its own
/// opening rows.</para>
/// </summary>
public sealed class GlSourceDocumentResolver
{
    /// <summary>What an Opening Balance posting shows in the Txn No column.</summary>
    public const string OpeningBalanceLabel = "Opening Balance";

    private readonly Dictionary<(DocumentType Type, Guid Id), SourceDocument> _documents;

    private GlSourceDocumentResolver(Dictionary<(DocumentType, Guid), SourceDocument> documents) =>
        _documents = documents;

    /// <summary>
    /// A document that has been deleted, or whose type is not one this resolver knows, degrades to
    /// a null Code rather than throwing -- a ledger that refuses to render because one document is
    /// missing is worse than one that says so in a cell.
    /// </summary>
    public SourceDocument? For(DocumentType type, Guid id) => _documents.GetValueOrDefault((type, id));

    public static async Task<GlSourceDocumentResolver> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyCollection<(DocumentType Type, Guid Id)> keys,
        CancellationToken cancellationToken)
    {
        var documents = new Dictionary<(DocumentType, Guid), SourceDocument>();

        List<Guid> IdsOf(DocumentType type) =>
            [.. keys.Where(k => k.Type == type).Select(k => k.Id).Distinct()];

        var invoiceIds = IdsOf(DocumentType.Invoice);
        if (invoiceIds.Count > 0)
        {
            var items = await db.Invoices
                .Where(x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.Invoice, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var creditNoteIds = IdsOf(DocumentType.CreditNote);
        if (creditNoteIds.Count > 0)
        {
            var items = await db.CreditNotes
                .Where(x => x.OrganizationId == organizationId && creditNoteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.CreditNote, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var purchaseBillIds = IdsOf(DocumentType.PurchaseBill);
        if (purchaseBillIds.Count > 0)
        {
            var items = await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && purchaseBillIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.PurchaseBill, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var expenseIds = IdsOf(DocumentType.Expense);
        if (expenseIds.Count > 0)
        {
            // Expense has no plain Reference -- SupplierInvoiceReference is its closest equivalent,
            // the same substitution TransactionApprovalQueryHandler and the Transaction list make.
            var items = await db.Expenses
                .Where(x => x.OrganizationId == organizationId && expenseIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.SupplierInvoiceReference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.Expense, x.Id)] = new SourceDocument(x.Code, x.SupplierInvoiceReference);
            }
        }

        var debitNoteIds = IdsOf(DocumentType.DebitNote);
        if (debitNoteIds.Count > 0)
        {
            var items = await db.DebitNotes
                .Where(x => x.OrganizationId == organizationId && debitNoteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.DebitNote, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var journalVoucherIds = IdsOf(DocumentType.JournalVoucher);
        if (journalVoucherIds.Count > 0)
        {
            var items = await db.JournalVouchers
                .Where(x => x.OrganizationId == organizationId && journalVoucherIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.JournalVoucher, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var cashTransferIds = IdsOf(DocumentType.CashTransfer);
        if (cashTransferIds.Count > 0)
        {
            var items = await db.CashTransfers
                .Where(x => x.OrganizationId == organizationId && cashTransferIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.CashTransfer, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var inventoryAdjustmentIds = IdsOf(DocumentType.InventoryAdjustment);
        if (inventoryAdjustmentIds.Count > 0)
        {
            var items = await db.InventoryAdjustments
                .Where(x => x.OrganizationId == organizationId && inventoryAdjustmentIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.InventoryAdjustment, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var paymentIds = IdsOf(DocumentType.Payment);
        if (paymentIds.Count > 0)
        {
            // Direction is carried through because the live product renders the two Directions of
            // the one Payment aggregate as two different Txn Types ("Customer Payment" and
            // "Supplier Payment"), and because it decides which of the two Angular detail routes a
            // row links to.
            var items = await db.Payments
                .Where(x => x.OrganizationId == organizationId && paymentIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference, x.Direction })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.Payment, x.Id)] = new SourceDocument(x.Code, x.Reference, x.Direction);
            }
        }

        var productionJournalIds = IdsOf(DocumentType.ProductionJournal);
        if (productionJournalIds.Count > 0)
        {
            var items = await db.ProductionJournals
                .Where(x => x.OrganizationId == organizationId && productionJournalIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.ProductionJournal, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        foreach (var id in IdsOf(DocumentType.OpeningBalance))
        {
            documents[(DocumentType.OpeningBalance, id)] = new SourceDocument(OpeningBalanceLabel, null);
        }

        return new GlSourceDocumentResolver(documents);
    }

    public sealed record SourceDocument(string Code, string? Reference, PaymentDirection? Direction = null);
}
