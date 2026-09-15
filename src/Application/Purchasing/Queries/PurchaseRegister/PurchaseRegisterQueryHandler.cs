using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Purchasing.Reports;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.PurchaseRegister;

public sealed class PurchaseRegisterQueryHandler(IAppDbContext db, ICurrentUserService currentUser) : IRequestHandler<PurchaseRegisterQuery, PurchaseRegisterDto>
{
    public async Task<PurchaseRegisterDto> Handle(PurchaseRegisterQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var rows = new List<PurchaseRegisterRowDto>();

        var purchaseBillQuery = db.PurchaseBills.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } purchaseBillContactId)
        {
            purchaseBillQuery = purchaseBillQuery.Where(x => x.ContactId == purchaseBillContactId);
        }

        // Phase 44 -- the Billing Location filter, which this half never applied. Phase 35b gave the
        // query its LocationId and taught PurchaseReturnReader to honour it, so the debit-note rows
        // narrowed and the purchase-bill rows did not: picking a location removed the returns from
        // the register and left every bill in it. The Purchase Master Report has applied it to both
        // its document queries since phase 32; this is the same two lines, missing.
        //
        // ReportLocationSweepGuardTests could not see it -- it asserts the *query record* accepts a
        // LocationId, which this one always did. That is phase-34b's rule exactly: a filter a screen
        // displays but does not apply is worse than no filter. The guard is widened in this phase.
        purchaseBillQuery = purchaseBillQuery.AtLocations(request.LocationId, reportLocations);

        var purchaseBills = await purchaseBillQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.Code, x.Date, x.SupplierInvoiceReference, x.IsImport, x.ImportDocumentNo,
                // Phase 44 (43 Decision D) -- carried for the fold to base currency below.
                x.ExchangeRate,
            })
            .ToListAsync(cancellationToken);
        var purchaseBillIds = purchaseBills.Select(x => x.Id).ToList();
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBillIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount, x.ExpenditureClassification, x.ProductId, x.Rate, x.VatRate })
            .ToListAsync(cancellationToken);

        var purchaseBillsById = purchaseBills.ToDictionary(x => x.Id);

        // Phase 44 (43 Decision D) -- <b>the register is filed in NPR, so it reports NPR.</b> The
        // same decision phase 43 applied to the Sales Register, applied to the purchase side: a
        // foreign bill used to contribute its own 100 to a statutory purchase book beside a domestic
        // bill's 100, with nothing on the page saying the two were not the same kind of thing.
        //
        // Folded per line, before Bucket, never on the finished buckets -- Bucketed.Total is the sum
        // of the other seven, so converting buckets independently would let Total stop equalling its
        // parts. In memory, because ExchangeRates.ToBase is a static call SQL Server cannot translate
        // and InMemory silently runs in C# (phase-34b).
        var purchaseBillBuckets = purchaseBillLines
            .GroupBy(x => x.PurchaseBillId)
            .ToDictionary(
                g => g.Key,
                g => PurchaseReturnReader.Bucket(
                    g.Select(l => (
                        ExchangeRates.ToBase(l.Amount, purchaseBillsById[g.Key].ExchangeRate),
                        ExchangeRates.ToBase(l.VatAmount, purchaseBillsById[g.Key].ExchangeRate),
                        l.ExpenditureClassification)),
                    purchaseBillsById[g.Key].IsImport));

        // Phase 26c: the debit-note half now comes from PurchaseReturnReader, which the new
        // Purchase Return Register also reads -- so the two registers show the same magnitudes for
        // the same notes by construction rather than by two implementations agreeing. This register
        // renders them negative; the return register renders them positive.
        var debitNotes = await PurchaseReturnReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken,
            request.LocationId, reportLocations);

        var contactIds = purchaseBills.Select(x => x.ContactId).Concat(debitNotes.Select(x => x.ContactId)).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        rows.AddRange(purchaseBills.Select(x =>
        {
            var b = purchaseBillBuckets.GetValueOrDefault(x.Id) ?? PurchaseReturnReader.Bucketed.Empty;
            var contact = contacts[x.ContactId];
            return new PurchaseRegisterRowDto(
                x.Date, DocumentType.PurchaseBill, x.Code, x.ImportDocumentNo, x.ContactId, contact.Name, contact.Pan,
                b.TaxExempt, b.NonCapitalLocalValue, b.NonCapitalLocalVat,
                b.NonCapitalImportValue, b.NonCapitalImportVat, b.CapitalValue, b.CapitalVat);
        }));

        rows.AddRange(debitNotes.Select(x =>
        {
            var b = x.Buckets;
            var contact = contacts[x.ContactId];
            return new PurchaseRegisterRowDto(
                x.Date, DocumentType.DebitNote, x.Code, null, x.ContactId, contact.Name, contact.Pan,
                -b.TaxExempt, -b.NonCapitalLocalValue, -b.NonCapitalLocalVat,
                -b.NonCapitalImportValue, -b.NonCapitalImportVat, -b.CapitalValue, -b.CapitalVat);
        }));

        var orderedRows = rows.OrderBy(x => x.Date).ThenBy(x => x.DocumentCode).ToList();
        var paged = request.ExportAll ? orderedRows.ToUnpagedResult() : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new PurchaseRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            orderedRows.Sum(x => x.TaxExemptValue),
            orderedRows.Sum(x => x.TaxableNonCapitalLocalValue), orderedRows.Sum(x => x.TaxableNonCapitalLocalVat),
            orderedRows.Sum(x => x.TaxableNonCapitalImportValue), orderedRows.Sum(x => x.TaxableNonCapitalImportVat),
            orderedRows.Sum(x => x.TaxableCapitalValue), orderedRows.Sum(x => x.TaxableCapitalVat));
    }

}
