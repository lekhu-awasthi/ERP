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

        // Phase 89 -- the bill half (and, since this phase, every approved expense) comes from PurchaseBookReader, which
        // the VAT return also reads, so a month of the return equals this register over that month by construction.
        // The reader carries phase 44's location filter on both types and phase 44's fold to base currency.
        var purchaseBook = await PurchaseBookReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken,
            request.LocationId, reportLocations);

        // Phase 26c: the debit-note half now comes from PurchaseReturnReader, which the new
        // Purchase Return Register also reads -- so the two registers show the same magnitudes for
        // the same notes by construction rather than by two implementations agreeing. This register
        // renders them negative; the return register renders them positive.
        var debitNotes = await PurchaseReturnReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken,
            request.LocationId, reportLocations);

        var contactIds = purchaseBook.Select(x => x.ContactId).Concat(debitNotes.Select(x => x.ContactId)).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        rows.AddRange(purchaseBook.Select(x =>
        {
            var b = x.Buckets;
            var contact = contacts[x.ContactId];
            return new PurchaseRegisterRowDto(
                x.Date, x.DocumentType, x.Code, x.SupplierInvoiceReference, x.ImportDocumentNo, x.ContactId, contact.Name, contact.Pan,
                b.TaxExempt, b.NonCapitalLocalValue, b.NonCapitalLocalVat,
                b.NonCapitalImportValue, b.NonCapitalImportVat, b.CapitalValue, b.CapitalVat);
        }));

        rows.AddRange(debitNotes.Select(x =>
        {
            var b = x.Buckets;
            var contact = contacts[x.ContactId];
            return new PurchaseRegisterRowDto(
                x.Date, DocumentType.DebitNote, x.Code, x.Code, null, x.ContactId, contact.Name, contact.Pan,
                Negated(b.TaxExempt), Negated(b.NonCapitalLocalValue), Negated(b.NonCapitalLocalVat),
                Negated(b.NonCapitalImportValue), Negated(b.NonCapitalImportVat), Negated(b.CapitalValue),
                Negated(b.CapitalVat));
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

    /// <summary>Phase 90 -- a debit note's figure, negated without giving a zero decimal's sign bit: <c>-0m</c> is
    /// serialised and written to a cell as "-0" (phase-26c bug #1), which the export showed on every note's empty
    /// bucket.</summary>
    private static decimal Negated(decimal value) => value == 0 ? 0 : -value;

}
