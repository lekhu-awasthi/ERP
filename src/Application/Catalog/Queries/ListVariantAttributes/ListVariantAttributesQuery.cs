using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListVariantAttributes;

/// <summary>The tenant-global attribute catalog. Server-paginated like every other list in this
/// codebase (phase-16c), even though a realistic catalog is small -- the live reference tenant
/// already carries 16.</summary>
public sealed record ListVariantAttributesQuery(
    Guid OrganizationId,
    bool ActiveOnly = false,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<VariantAttributeResult>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VariantAttributeView;
}
