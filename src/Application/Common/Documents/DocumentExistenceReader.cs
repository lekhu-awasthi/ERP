using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Documents;

/// <summary>
/// Phase 27a -- resolves "does document (type, id) exist in this organization?" for every document
/// type anything can be attached to.
///
/// <para>Phases 19 and 20a each hand-wrote a two-arm <c>EnsureDocumentExistsAsync</c> switch inside
/// their own handler. Phase 27a needs the same question answered by six callers (custom fields,
/// reporting tags, custom status, tasks, attachments, comments) across 17 document types; six copies
/// of a 17-arm switch is six chances for one of them to quietly not know about a type. So there is
/// one, here.</para>
///
/// <para>Note this is deliberately an existence check, not a fetch: no caller needs the document
/// itself, and <c>AnyAsync</c> keeps it a single cheap EXISTS per call rather than materialising an
/// aggregate and its collections.</para>
/// </summary>
public static class DocumentExistenceReader
{
    /// <summary>
    /// Throws <see cref="NotFoundException"/> when the document does not exist in this organization,
    /// and <see cref="ArgumentOutOfRangeException"/> when the type is not one anything can attach to
    /// (a wiring bug -- <c>AuthorizationBehavior</c> has already rejected an unknown type, since
    /// <see cref="DocumentPermissions"/> throws for exactly the same set).
    /// </summary>
    public static async Task EnsureExistsAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType documentType,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var exists = documentType switch
        {
            DocumentType.Quotation => await db.Quotations
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.SalesOrder => await db.SalesOrders
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.Invoice => await db.Invoices
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.CreditNote => await db.CreditNotes
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.Payment => await db.Payments
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.PurchaseOrder => await db.PurchaseOrders
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.PurchaseBill => await db.PurchaseBills
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.Expense => await db.Expenses
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.DebitNote => await db.DebitNotes
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.JournalVoucher => await db.JournalVouchers
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.CashTransfer => await db.CashTransfers
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.WarehouseTransfer => await db.WarehouseTransfers
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.InventoryAdjustment => await db.InventoryAdjustments
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.ProductionOrder => await db.ProductionOrders
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.ProductionJournal => await db.ProductionJournals
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),

            // The two non-transactional taggables. Each is one row of the Opening Balances screen,
            // keyed by its own Id -- the same identity GlJournalEntry.SourceDocumentId already uses.
            DocumentType.OpeningBalance => await db.OpeningBalanceLines
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.OpeningStock => await db.OpeningStockLines
                .AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(documentType),
                documentType,
                $"Nothing can be attached to {documentType} -- see DocumentMechanisms.NotApplicableReasons."),
        };

        if (!exists)
        {
            throw new NotFoundException($"{documentType} not found.");
        }
    }
}
