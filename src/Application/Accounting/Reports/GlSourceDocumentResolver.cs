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
/// them therefore has to join back, and there are <b>fourteen</b> source types that post GL
/// (grep-confirmed against every <c>GlJournalEntry.Post</c> call site: Invoice, CreditNote,
/// PurchaseBill, Expense, DebitNote, JournalVoucher, CashTransfer, InventoryAdjustment, Payment,
/// ProductionJournal, OpeningBalance, and -- from phase 37, for a cost catch-up only --
/// WarehouseTransfer and OpeningStock; and from phase 61 a till's PosSession, for its drawer's cash
/// movements and its close's over/short. Quotation, SalesOrder and PurchaseOrder post nothing).
/// Writing that join out once here beats writing it three times.</para>
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

    /// <summary>Phase 37 -- what an Opening Stock cost catch-up shows in the Txn No column.</summary>
    public const string OpeningStockLabel = "Opening Stock";

    private readonly Dictionary<(DocumentType Type, Guid Id), SourceDocument> _documents;

    private GlSourceDocumentResolver(Dictionary<(DocumentType, Guid), SourceDocument> documents) =>
        _documents = documents;

    /// <summary>
    /// A document that has been deleted, or whose type is not one this resolver knows, degrades to
    /// a null Code rather than throwing -- a ledger that refuses to render because one document is
    /// missing is worse than one that says so in a cell.
    ///
    /// <para>An Opening Balance or Opening Stock line is not a numbered document (an Opening Stock line is keyed by
    /// organization, product and warehouse, exactly like an Opening Balance line by account), so either reports its
    /// literal label without a lookup.</para>
    /// </summary>
    public SourceDocument? For(DocumentType type, Guid id) => type switch
    {
        DocumentType.OpeningBalance => new SourceDocument(OpeningBalanceLabel, null),
        DocumentType.OpeningStock => new SourceDocument(OpeningStockLabel, null),
        _ => _documents.GetValueOrDefault((type, id)),
    };

    public static Task<GlSourceDocumentResolver> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyCollection<(DocumentType Type, Guid Id)> keys,
        CancellationToken cancellationToken) =>
        LoadCoreAsync(
            db,
            organizationId,
            type => [.. keys.Where(k => k.Type == type).Select(k => k.Id).Distinct()],
            cancellationToken);

    /// <summary>
    /// Phase 80 -- every document of the named types in the organization, for a reader whose page is too long to hand
    /// back to SQL as a list of ids: the export's two ledger sheets run to 50,000 rows, and an id list that long is
    /// phase 42's <c>OPENJSON</c> parameter costing more than the scan it was meant to narrow. A type's whole list is a
    /// (Id, Code, Reference) row per document, bounded by the tenant and read once per sheet.
    /// </summary>
    public static Task<GlSourceDocumentResolver> LoadEveryAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlySet<DocumentType> types,
        CancellationToken cancellationToken) =>
        LoadCoreAsync(db, organizationId, type => types.Contains(type) ? null : [], cancellationToken);

    /// <param name="idsOf">The ids to load for a type: an empty list skips the type, and <c>null</c> loads all of it.
    /// Each type composes its id filter as a second <c>Where</c> only when it has one (phase 33's rule: never hand EF a
    /// null list inside an <c>||</c>).</param>
    private static async Task<GlSourceDocumentResolver> LoadCoreAsync(
        IAppDbContext db,
        Guid organizationId,
        Func<DocumentType, List<Guid>?> idsOf,
        CancellationToken cancellationToken)
    {
        var documents = new Dictionary<(DocumentType, Guid), SourceDocument>();

        var invoiceIds = idsOf(DocumentType.Invoice);
        if (invoiceIds is not { Count: 0 })
        {
            var query = db.Invoices.Where(x => x.OrganizationId == organizationId);
            if (invoiceIds is not null)
            {
                query = query.Where(x => invoiceIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.Invoice, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var creditNoteIds = idsOf(DocumentType.CreditNote);
        if (creditNoteIds is not { Count: 0 })
        {
            var query = db.CreditNotes.Where(x => x.OrganizationId == organizationId);
            if (creditNoteIds is not null)
            {
                query = query.Where(x => creditNoteIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.CreditNote, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var purchaseBillIds = idsOf(DocumentType.PurchaseBill);
        if (purchaseBillIds is not { Count: 0 })
        {
            var query = db.PurchaseBills.Where(x => x.OrganizationId == organizationId);
            if (purchaseBillIds is not null)
            {
                query = query.Where(x => purchaseBillIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.PurchaseBill, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var expenseIds = idsOf(DocumentType.Expense);
        if (expenseIds is not { Count: 0 })
        {
            // Expense has no plain Reference -- SupplierInvoiceReference is its closest equivalent,
            // the same substitution TransactionApprovalQueryHandler and the Transaction list make.
            var query = db.Expenses.Where(x => x.OrganizationId == organizationId);
            if (expenseIds is not null)
            {
                query = query.Where(x => expenseIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.SupplierInvoiceReference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.Expense, x.Id)] = new SourceDocument(x.Code, x.SupplierInvoiceReference);
            }
        }

        var debitNoteIds = idsOf(DocumentType.DebitNote);
        if (debitNoteIds is not { Count: 0 })
        {
            var query = db.DebitNotes.Where(x => x.OrganizationId == organizationId);
            if (debitNoteIds is not null)
            {
                query = query.Where(x => debitNoteIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.DebitNote, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var journalVoucherIds = idsOf(DocumentType.JournalVoucher);
        if (journalVoucherIds is not { Count: 0 })
        {
            var query = db.JournalVouchers.Where(x => x.OrganizationId == organizationId);
            if (journalVoucherIds is not null)
            {
                query = query.Where(x => journalVoucherIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.JournalVoucher, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var cashTransferIds = idsOf(DocumentType.CashTransfer);
        if (cashTransferIds is not { Count: 0 })
        {
            var query = db.CashTransfers.Where(x => x.OrganizationId == organizationId);
            if (cashTransferIds is not null)
            {
                query = query.Where(x => cashTransferIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.CashTransfer, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var inventoryAdjustmentIds = idsOf(DocumentType.InventoryAdjustment);
        if (inventoryAdjustmentIds is not { Count: 0 })
        {
            var query = db.InventoryAdjustments.Where(x => x.OrganizationId == organizationId);
            if (inventoryAdjustmentIds is not null)
            {
                query = query.Where(x => inventoryAdjustmentIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.InventoryAdjustment, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        var paymentIds = idsOf(DocumentType.Payment);
        if (paymentIds is not { Count: 0 })
        {
            // Direction is carried through because the live product renders the two Directions of
            // the one Payment aggregate as two different Txn Types ("Customer Payment" and
            // "Supplier Payment"), and because it decides which of the two Angular detail routes a
            // row links to.
            var query = db.Payments.Where(x => x.OrganizationId == organizationId);
            if (paymentIds is not null)
            {
                query = query.Where(x => paymentIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference, x.Direction })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.Payment, x.Id)] = new SourceDocument(x.Code, x.Reference, x.Direction);
            }
        }

        var productionJournalIds = idsOf(DocumentType.ProductionJournal);
        if (productionJournalIds is not { Count: 0 })
        {
            var query = db.ProductionJournals.Where(x => x.OrganizationId == organizationId);
            if (productionJournalIds is not null)
            {
                query = query.Where(x => productionJournalIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.ProductionJournal, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        // Phase 37 -- two types that posted nothing until this phase now can. A Warehouse Transfer
        // and an Opening Stock line both move value without an accounting entry in the ordinary
        // case, but either can be the receipt that covers a shortfall, and the cost catch-up that
        // follows is a real posting against them (see StockCostCatchUp). Without these two blocks
        // such a row rendered with an empty Txn No in the Journal report and the two general
        // ledgers -- which the resolver degrades to rather than throwing, so nothing would have
        // told us.
        var warehouseTransferIds = idsOf(DocumentType.WarehouseTransfer);
        if (warehouseTransferIds is not { Count: 0 })
        {
            var query = db.WarehouseTransfers.Where(x => x.OrganizationId == organizationId);
            if (warehouseTransferIds is not null)
            {
                query = query.Where(x => warehouseTransferIds.Contains(x.Id));
            }

            var items = await query
                .Select(x => new { x.Id, x.Code, x.Reference })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.WarehouseTransfer, x.Id)] = new SourceDocument(x.Code, x.Reference);
            }
        }

        // Phase 61 -- a till session posts its cash movements and its close's over/short against
        // itself, so a GL row from one names the session (SES0001), with the location as reference.
        var posSessionIds = idsOf(DocumentType.PosSession);
        if (posSessionIds is not { Count: 0 })
        {
            var sessions = db.PosSessions.Where(x => x.OrganizationId == organizationId);
            if (posSessionIds is not null)
            {
                sessions = sessions.Where(x => posSessionIds.Contains(x.Id));
            }

            var items = await (
                    from session in sessions
                    join location in db.BillingLocations on session.BillingLocationId equals location.Id
                    select new { session.Id, session.Code, LocationName = location.Name })
                .ToListAsync(cancellationToken);
            foreach (var x in items)
            {
                documents[(DocumentType.PosSession, x.Id)] = new SourceDocument(x.Code, x.LocationName);
            }
        }

        return new GlSourceDocumentResolver(documents);
    }

    public sealed record SourceDocument(string Code, string? Reference, PaymentDirection? Direction = null);
}
