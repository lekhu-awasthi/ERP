using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.BackfillDocumentLocations;

public sealed class BackfillDocumentLocationsCommandHandler(IAppDbContext db)
    : IRequestHandler<BackfillDocumentLocationsCommand, BackfillDocumentLocationsResult>
{
    public async Task<BackfillDocumentLocationsResult> Handle(
        BackfillDocumentLocationsCommand request, CancellationToken cancellationToken)
    {
        var headOfficeId = await db.BillingLocations
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.LocationType == BillingLocationType.HeadOffice
                        && x.IsActive)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException(
                "This organization has no active HeadOffice location to assign, so there is nothing "
                + "to backfill to.");

        // The tenant's current scope decides which types *should* carry a location. Backfilling a
        // type that is out of scope would write a value the write path deliberately leaves null --
        // the setting would then be a lie in the opposite direction.
        var mode = await db.TenantSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => (LocationScopeMode?)x.LocationScopeMode)
            .SingleOrDefaultAsync(cancellationToken) ?? LocationScopeMode.SalesTransactionsOnly;

        var inScope = DocumentLocationScope.For(mode);
        const string OrganizationIdProperty = nameof(Domain.Sales.Invoice.OrganizationId);
        var counts = new List<BackfilledDocumentTypeCount>();

        async Task BackfillAsync<T>(DocumentType documentType, IQueryable<T> set, Action<T> assign)
            where T : class
        {
            if (!inScope.Contains(documentType))
            {
                return;
            }

            // <b>EF.Property, not a captured selector.</b> A generic helper cannot write
            // `x => x.OrganizationId` -- the seventeen aggregates share no interface, and handing
            // `Where` a captured Func is the phase-2/9/25 gotcha in its purest form: InMemory
            // evaluates it in C# so every handler test passes, and SQL Server cannot translate it so
            // the endpoint 500s. EF.Property resolves by name at translation time and is what
            // ReportLocationFilter already does over this very column, on all seventeen types.
            //
            // Through the aggregate's own BackfillLocation, not SetLocation: that one is draft-only
            // by design, and the whole population this command exists for is Approved historical
            // documents. BackfillLocation states the narrower rule -- fills a null, refuses to move
            // an assigned location -- so the guard is not weakened, it is stepped around where it
            // provably does not apply. Not ExecuteUpdate, because the
            // property is private-set by design, and the InMemory provider every handler test uses
            // does not support ExecuteUpdate at all -- a backfill that cannot be tested is the kind
            // that goes wrong quietly. These are only the rows with no location, which on a real
            // tenant is a bounded historical set, not the whole table.
            var rows = await set
                .Where(x => EF.Property<Guid>(x, OrganizationIdProperty) == request.OrganizationId
                            && EF.Property<Guid?>(x, ReportLocationFilter.LocationIdProperty) == null)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                return;
            }

            foreach (var row in rows)
            {
                assign(row);
            }

            counts.Add(new BackfilledDocumentTypeCount(documentType, rows.Count));
        }

        await BackfillAsync(DocumentType.Quotation, db.Quotations, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.SalesOrder, db.SalesOrders, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.Invoice, db.Invoices, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.CreditNote, db.CreditNotes, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.PurchaseOrder, db.PurchaseOrders, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.PurchaseBill, db.PurchaseBills, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.DebitNote, db.DebitNotes, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.Expense, db.Expenses, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.Payment, db.Payments, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.JournalVoucher, db.JournalVouchers, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.CashTransfer, db.CashTransfers, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.WarehouseTransfer, db.WarehouseTransfers, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.InventoryAdjustment, db.InventoryAdjustments, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.ProductionOrder, db.ProductionOrders, x => x.BackfillLocation(headOfficeId));
        await BackfillAsync(DocumentType.ProductionJournal, db.ProductionJournals, x => x.BackfillLocation(headOfficeId));

        // The two lifecycle-free "day zero" kinds. They have no SetLocation of their own -- the whole
        // row is editable in place through Update -- so the backfill restates the values it just read
        // rather than inventing a mutator for one caller.
        await BackfillAsync(
            DocumentType.OpeningBalance, db.OpeningBalanceLines,
            x => x.Update(x.Debit, x.Credit, x.CurrencyCode, x.ExchangeRate, headOfficeId));
        await BackfillAsync(
            DocumentType.OpeningStock, db.OpeningStockLines,
            x => x.Update(x.Quantity, x.Rate, headOfficeId));

        await db.SaveChangesAsync(cancellationToken);

        return new BackfillDocumentLocationsResult(
            headOfficeId, counts.Sum(x => x.Updated), counts);
    }
}
