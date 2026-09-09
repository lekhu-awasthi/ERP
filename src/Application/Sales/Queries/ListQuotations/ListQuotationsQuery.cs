using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListQuotations;

public sealed record ListQuotationsQuery(
    Guid OrganizationId,
    QuotationStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<Quotation>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery
{
    public string PermissionKey => PermissionKeys.QuotationView;
}
