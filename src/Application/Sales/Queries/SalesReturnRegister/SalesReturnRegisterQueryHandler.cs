using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.SalesReturnRegister;

public sealed class SalesReturnRegisterQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SalesReturnRegisterQuery, SalesReturnRegisterDto>
{
    public async Task<SalesReturnRegisterDto> Handle(
        SalesReturnRegisterQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var creditNotes = await SalesReturnReader.LoadAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.ContactId, cancellationToken,
            request.LocationId, reportLocations);

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
