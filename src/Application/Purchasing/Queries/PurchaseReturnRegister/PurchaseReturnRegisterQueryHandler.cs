using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.PurchaseReturnRegister;

public sealed class PurchaseReturnRegisterQueryHandler(IAppDbContext db)
    : IRequestHandler<PurchaseReturnRegisterQuery, PurchaseReturnRegisterDto>
{
    public async Task<PurchaseReturnRegisterDto> Handle(
        PurchaseReturnRegisterQuery request, CancellationToken cancellationToken)
    {
        var debitNotes = await PurchaseReturnReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken);

        var contactIds = debitNotes.Select(x => x.ContactId).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = debitNotes
            .Select(x =>
            {
                var contact = contacts[x.ContactId];
                var b = x.Buckets;
                return new PurchaseReturnRegisterRowDto(
                    x.Date,
                    x.Code,
                    // See the DTO: a Debit Note has no import declaration of its own.
                    ImportDeclarationNo: null,
                    x.ContactId,
                    contact.Name,
                    contact.Pan,
                    b.Total,
                    b.TaxExempt,
                    b.NonCapitalLocalValue,
                    b.NonCapitalLocalVat,
                    b.NonCapitalImportValue,
                    b.NonCapitalImportVat,
                    b.CapitalValue,
                    b.CapitalVat);
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        // The live register does carry a footer Total, unlike phase-26a's GL reports -- and it can,
        // because every column here is the same unit of account. Computed over the whole filtered
        // set, not the page (phase-16c bug #1).
        return new PurchaseReturnRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(x => x.TotalReturnValue),
            rows.Sum(x => x.TaxExemptValue),
            rows.Sum(x => x.TaxableNonCapitalLocalValue),
            rows.Sum(x => x.TaxableNonCapitalLocalVat),
            rows.Sum(x => x.TaxableNonCapitalImportValue),
            rows.Sum(x => x.TaxableNonCapitalImportVat),
            rows.Sum(x => x.TaxableCapitalValue),
            rows.Sum(x => x.TaxableCapitalVat));
    }
}
