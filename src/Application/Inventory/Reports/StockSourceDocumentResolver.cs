using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Reports;

/// <summary>
/// Resolves a set of <c>StockMovement</c> rows back to the documents that caused them -- the Type,
/// #No, Reference No and Contact columns the Inventory Ledger and Inventory Master reports show on
/// every row. Phase 26c's counterpart to phase-26a's <c>GlSourceDocumentResolver</c>, and it exists
/// for the identical reason: <c>StockMovement</c> stores <c>SourceDocumentType</c> and
/// <c>SourceDocumentId</c> and nothing else about the document, so any report naming the document
/// has to join back.
///
/// <para><b>It is a separate resolver rather than a widening of the GL one, because the two sets of
/// document types barely overlap.</b> Eight types move stock -- Invoice, CreditNote, PurchaseBill,
/// DebitNote, InventoryAdjustment, WarehouseTransfer, ProductionJournal and OpeningStock
/// (grep-confirmed against every <c>IStockLedgerService</c> call site) -- while eleven post GL.
/// WarehouseTransfer and OpeningStock move stock without posting anything the GL resolver knows;
/// Payment, Expense, JournalVoucher, CashTransfer and OpeningBalance post GL without touching
/// stock. Merging them would give each caller five types it must never see.</para>
///
/// <para><b>Contact is genuinely optional, and that is not a gap.</b> A WarehouseTransfer, an
/// InventoryAdjustment, a ProductionJournal and an opening stock line have no counterparty at all;
/// the live Inventory Master leaves the Contact cell blank on exactly those rows.</para>
///
/// <para>One batched round trip per type, each its own concrete <c>Where(ids.Contains(...))</c>
/// rather than a generic helper over a captured <c>Func</c> -- phase-9 bug #1 -- and skipped
/// entirely when no row of that type is present.</para>
/// </summary>
public sealed class StockSourceDocumentResolver
{
    /// <summary>What an opening stock movement shows in the #No column; it is not a numbered
    /// document, exactly as <c>GlSourceDocumentResolver.OpeningBalanceLabel</c> is not.</summary>
    public const string OpeningStockLabel = "Opening Stock";

    private readonly Dictionary<(DocumentType Type, Guid Id), SourceDocument> _documents;

    private StockSourceDocumentResolver(Dictionary<(DocumentType, Guid), SourceDocument> documents) =>
        _documents = documents;

    /// <summary>Null for a document that has been deleted or whose type is not one of the eight --
    /// the caller renders a blank cell rather than failing the whole report.</summary>
    public SourceDocument? For(DocumentType type, Guid id) => _documents.GetValueOrDefault((type, id));

    public static async Task<StockSourceDocumentResolver> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyCollection<(DocumentType Type, Guid Id)> keys,
        CancellationToken cancellationToken)
    {
        var documents = new Dictionary<(DocumentType, Guid), SourceDocument>();
        var contactIds = new HashSet<Guid>();

        List<Guid> IdsOf(DocumentType type) =>
            [.. keys.Where(k => k.Type == type).Select(k => k.Id).Distinct()];

        var invoiceIds = IdsOf(DocumentType.Invoice);
        if (invoiceIds.Count > 0)
        {
            foreach (var row in await db.Invoices
                .Where(x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference, x.ContactId })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.Invoice, row.Id)] = new SourceDocument(row.Code, row.Reference, row.ContactId);
                contactIds.Add(row.ContactId);
            }
        }

        var creditNoteIds = IdsOf(DocumentType.CreditNote);
        if (creditNoteIds.Count > 0)
        {
            foreach (var row in await db.CreditNotes
                .Where(x => x.OrganizationId == organizationId && creditNoteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference, x.ContactId })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.CreditNote, row.Id)] = new SourceDocument(row.Code, row.Reference, row.ContactId);
                contactIds.Add(row.ContactId);
            }
        }

        var purchaseBillIds = IdsOf(DocumentType.PurchaseBill);
        if (purchaseBillIds.Count > 0)
        {
            foreach (var row in await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && purchaseBillIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference, x.ContactId })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.PurchaseBill, row.Id)] = new SourceDocument(row.Code, row.Reference, row.ContactId);
                contactIds.Add(row.ContactId);
            }
        }

        var debitNoteIds = IdsOf(DocumentType.DebitNote);
        if (debitNoteIds.Count > 0)
        {
            foreach (var row in await db.DebitNotes
                .Where(x => x.OrganizationId == organizationId && debitNoteIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference, x.ContactId })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.DebitNote, row.Id)] = new SourceDocument(row.Code, row.Reference, row.ContactId);
                contactIds.Add(row.ContactId);
            }
        }

        var adjustmentIds = IdsOf(DocumentType.InventoryAdjustment);
        if (adjustmentIds.Count > 0)
        {
            foreach (var row in await db.InventoryAdjustments
                .Where(x => x.OrganizationId == organizationId && adjustmentIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.InventoryAdjustment, row.Id)] = new SourceDocument(row.Code, row.Reference, null);
            }
        }

        var transferIds = IdsOf(DocumentType.WarehouseTransfer);
        if (transferIds.Count > 0)
        {
            foreach (var row in await db.WarehouseTransfers
                .Where(x => x.OrganizationId == organizationId && transferIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.WarehouseTransfer, row.Id)] = new SourceDocument(row.Code, row.Reference, null);
            }
        }

        var productionJournalIds = IdsOf(DocumentType.ProductionJournal);
        if (productionJournalIds.Count > 0)
        {
            foreach (var row in await db.ProductionJournals
                .Where(x => x.OrganizationId == organizationId && productionJournalIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken))
            {
                documents[(DocumentType.ProductionJournal, row.Id)] = new SourceDocument(row.Code, row.Reference, null);
            }
        }

        // Opening stock: an OpeningStockLine is keyed by (Organization, Product, Warehouse) and
        // carries no code, reference or contact -- it is a starting position, not a document.
        foreach (var id in IdsOf(DocumentType.OpeningStock))
        {
            documents[(DocumentType.OpeningStock, id)] = new SourceDocument(OpeningStockLabel, null, null);
        }

        if (contactIds.Count > 0)
        {
            var names = await db.Contacts
                .Where(c => contactIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

            foreach (var key in documents.Keys.ToList())
            {
                var document = documents[key];
                if (document.ContactId is { } contactId)
                {
                    documents[key] = document with { ContactName = names.GetValueOrDefault(contactId) };
                }
            }
        }

        return new StockSourceDocumentResolver(documents);
    }

    public sealed record SourceDocument(string Code, string? Reference, Guid? ContactId, string? ContactName = null);
}
