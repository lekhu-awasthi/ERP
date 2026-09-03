using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Reports;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.PurchaseRegister;

public sealed class PurchaseRegisterQueryHandler(IAppDbContext db) : IRequestHandler<PurchaseRegisterQuery, PurchaseRegisterDto>
{
    public async Task<PurchaseRegisterDto> Handle(PurchaseRegisterQuery request, CancellationToken cancellationToken)
    {
        var rows = new List<PurchaseRegisterRowDto>();

        var purchaseBillQuery = db.PurchaseBills.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } purchaseBillContactId)
        {
            purchaseBillQuery = purchaseBillQuery.Where(x => x.ContactId == purchaseBillContactId);
        }

        var purchaseBills = await purchaseBillQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.Code, x.Date, x.SupplierInvoiceReference, x.IsImport, x.ImportDocumentNo,
            })
            .ToListAsync(cancellationToken);
        var purchaseBillIds = purchaseBills.Select(x => x.Id).ToList();
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBillIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount, x.ExpenditureClassification, x.ProductId, x.Rate, x.VatRate })
            .ToListAsync(cancellationToken);

        var purchaseBillsById = purchaseBills.ToDictionary(x => x.Id);
        var purchaseBillBuckets = purchaseBillLines
            .GroupBy(x => x.PurchaseBillId)
            .ToDictionary(
                g => g.Key,
                g => PurchaseReturnReader.Bucket(
                    g.Select(l => (l.Amount, l.VatAmount, l.ExpenditureClassification)), purchaseBillsById[g.Key].IsImport));

        // Phase 26c: the debit-note half now comes from PurchaseReturnReader, which the new
        // Purchase Return Register also reads -- so the two registers show the same magnitudes for
        // the same notes by construction rather than by two implementations agreeing. This register
        // renders them negative; the return register renders them positive.
        var debitNotes = await PurchaseReturnReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken);

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
