using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.MigratedPurchaseRegister;

public sealed class MigratedPurchaseRegisterQueryHandler(IAppDbContext db)
    : IRequestHandler<MigratedPurchaseRegisterQuery, PurchaseRegisterDto>
{
    public async Task<PurchaseRegisterDto> Handle(
        MigratedPurchaseRegisterQuery request, CancellationToken cancellationToken)
    {
        var query = db.MigratedPurchaseRegisterEntries.Where(x =>
            x.OrganizationId == request.OrganizationId
            && x.Date >= request.FromDate && x.Date <= request.ToDate);

        if (!string.IsNullOrWhiteSpace(request.PartySearch))
        {
            var term = request.PartySearch.Trim();
            query = query.Where(x =>
                x.PartyName.Contains(term) || (x.PartyPan != null && x.PartyPan.Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.Date).ThenBy(x => x.DocumentCode)
            .Select(x => new PurchaseRegisterRowDto(
                x.Date,
                DocumentType.MigratedPurchaseEntry,
                x.DocumentCode,
                x.ImportDeclarationNo,
                x.ContactId,
                x.PartyName,
                x.PartyPan,
                x.TaxExemptValue,
                x.TaxableNonCapitalLocalValue,
                x.TaxableNonCapitalLocalVat,
                x.TaxableNonCapitalImportValue,
                x.TaxableNonCapitalImportVat,
                x.TaxableCapitalValue,
                x.TaxableCapitalVat))
            .ToListAsync(cancellationToken);

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        // Totals over the full filtered set, not the page (phase-16c bug #1).
        return new PurchaseRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(x => x.TaxExemptValue),
            rows.Sum(x => x.TaxableNonCapitalLocalValue),
            rows.Sum(x => x.TaxableNonCapitalLocalVat),
            rows.Sum(x => x.TaxableNonCapitalImportValue),
            rows.Sum(x => x.TaxableNonCapitalImportVat),
            rows.Sum(x => x.TaxableCapitalValue),
            rows.Sum(x => x.TaxableCapitalVat));
    }
}
