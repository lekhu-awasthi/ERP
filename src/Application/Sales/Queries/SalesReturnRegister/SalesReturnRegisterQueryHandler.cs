using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.SalesReturnRegister;

public sealed class SalesReturnRegisterQueryHandler(IAppDbContext db)
    : IRequestHandler<SalesReturnRegisterQuery, SalesReturnRegisterDto>
{
    public async Task<SalesReturnRegisterDto> Handle(
        SalesReturnRegisterQuery request, CancellationToken cancellationToken)
    {
        var creditNotes = await SalesReturnReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken);

        var contactIds = creditNotes.Select(x => x.ContactId).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = creditNotes
            .Select(x =>
            {
                var contact = contacts[x.ContactId];
                var b = x.Buckets;
                return new SalesReturnRegisterRowDto(
                    x.Date, x.Code, x.ContactId, contact.Name, contact.Pan,
                    b.Total, b.TaxExempt, b.Taxable, b.Vat);
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new SalesReturnRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(x => x.TotalReturnValue),
            rows.Sum(x => x.TaxExemptReturnValue),
            rows.Sum(x => x.TaxableReturnValue),
            rows.Sum(x => x.VatAmount));
    }
}
