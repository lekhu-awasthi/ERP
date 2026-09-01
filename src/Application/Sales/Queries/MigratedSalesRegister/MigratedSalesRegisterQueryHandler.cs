using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.MigratedSalesRegister;

public sealed class MigratedSalesRegisterQueryHandler(IAppDbContext db)
    : IRequestHandler<MigratedSalesRegisterQuery, SalesRegisterDto>
{
    public async Task<SalesRegisterDto> Handle(MigratedSalesRegisterQuery request, CancellationToken cancellationToken)
    {
        // Manual OrganizationId filter, as everywhere else -- there is no EF global query filter in
        // this codebase, and the tenant-isolation tests are what hold this line.
        var query = db.MigratedSalesRegisterEntries.Where(x =>
            x.OrganizationId == request.OrganizationId
            && x.Date >= request.FromDate && x.Date <= request.ToDate);

        if (!string.IsNullOrWhiteSpace(request.PartySearch))
        {
            // String.Contains rather than EF.Functions.Like: both translate to LIKE '%term%' on SQL
            // Server, but the InMemory provider cannot translate EF.Functions at all, and this
            // handler's tenant-isolation and paging tests run on InMemory. Case-insensitivity comes
            // from the database's own collation, as it does for every other comparison here.
            var term = request.PartySearch.Trim();
            query = query.Where(x =>
                x.PartyName.Contains(term) || (x.PartyPan != null && x.PartyPan.Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.Date).ThenBy(x => x.DocumentCode)
            .Select(x => new SalesRegisterRowDto(
                x.Date,
                DocumentType.MigratedSalesEntry,
                x.DocumentCode,
                x.ContactId,
                x.PartyName,
                x.PartyPan,
                x.TotalValue,
                x.TaxExemptValue,
                x.TaxableValue,
                x.VatAmount,
                x.ExportValue,
                x.ExportCountry,
                x.ExportDeclarationNo,
                x.ExportDeclarationDate))
            .ToListAsync(cancellationToken);

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        // Footer totals are summed over the whole filtered set, never the current page -- the bug
        // phase-16c found in four pre-existing report pages. `rows` is the full filtered set here;
        // `paged.Items` is one page of it.
        return new SalesRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(x => x.TotalValue), rows.Sum(x => x.TaxExemptValue),
            rows.Sum(x => x.TaxableValue), rows.Sum(x => x.VatAmount));
    }
}
