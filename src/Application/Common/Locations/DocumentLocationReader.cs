using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Locations;

/// <summary>
/// Phase 32b -- reads the billing location off an existing document, given only its type and id.
/// One switch, in one file, called by <see cref="LocationScopeResolver"/> on behalf of
/// <c>AuthorizationBehavior</c>.
///
/// <para>Structurally this is <c>LockDateBehavior.ResolveDocumentDateAsync</c>'s sibling and follows
/// the same precedent phase 12's <c>TransactionApprovalQueryHandler</c> set for cross-document-type
/// dispatch in this codebase: <b>concrete blocks, not one generic helper</b>. A generic
/// <c>IQueryable</c> helper taking a selector is exactly what phase-9 bug #1 and phase-2 bug #5 warn
/// against -- a captured <c>Func</c> cannot be translated, and the failure is a 500 at runtime rather
/// than a compile error.</para>
///
/// <para>The two opening-balance kinds are absent on purpose: they are line-level rows rather than
/// documents with an id of their own, they carry no Draft/Approve lifecycle, and no request declares
/// them as an <see cref="ILocationScopedDocument"/> target. Their Create/Update commands are
/// location-scoped through <see cref="ILocationBearingCommand"/> like every other write, which is
/// the whole of their surface.</para>
/// </summary>
public static class DocumentLocationReader
{
    /// <summary>A one-field projection, so <c>SingleOrDefaultAsync</c> can return <c>null</c> for
    /// "no such row" while the field itself carries "row exists, no location".</summary>
    private sealed record LocationBox(Guid? LocationId);

    /// <summary>
    /// The document's stored location. <b>The two kinds of "no location" are deliberately different
    /// answers</b>, because the caller turns one into a 404 and the other into a 403:
    /// <c>Found=false</c> is a row that does not exist (or a type this reader does not cover), and
    /// <c>Found=true, LocationId=null</c> is a real row written while the tenant's
    /// <c>LocationScopeMode</c> excluded its type. Collapsing them would either 403 a nonexistent id
    /// -- which breaks phase-31's both-directions proof, since the 404 leg is what shows the caller
    /// holds the pipeline key -- or let a location-scoped caller act on every location-less document
    /// in the tenant.
    /// </summary>
    public static async Task<(bool Found, Guid? LocationId)> ReadAsync(
        IAppDbContext db,
        DocumentType documentType,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var box = await ReadBoxAsync(db, documentType, documentId, cancellationToken);

        return box is null ? (false, null) : (true, box.LocationId);
    }

    private static async Task<LocationBox?> ReadBoxAsync(
        IAppDbContext db,
        DocumentType documentType,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return documentType switch
        {
            DocumentType.Quotation => await db.Quotations.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.SalesOrder => await db.SalesOrders.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.Invoice => await db.Invoices.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.CreditNote => await db.CreditNotes.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.Payment => await db.Payments.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.PurchaseOrder => await db.PurchaseOrders.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.PurchaseBill => await db.PurchaseBills.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.Expense => await db.Expenses.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.DebitNote => await db.DebitNotes.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.JournalVoucher => await db.JournalVouchers.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.CashTransfer => await db.CashTransfers.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.WarehouseTransfer => await db.WarehouseTransfers.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.InventoryAdjustment => await db.InventoryAdjustments.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.ProductionOrder => await db.ProductionOrders.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            DocumentType.ProductionJournal => await db.ProductionJournals.Where(x => x.Id == documentId)
                .Select(x => new LocationBox(x.LocationId)).SingleOrDefaultAsync(cancellationToken),
            _ => null,
        };
    }
}
