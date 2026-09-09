using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ListOpeningStockLines;

public sealed class ListProductOpeningBalancesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListProductOpeningBalancesQuery, PagedResult<ProductOpeningBalanceDto>>
{
    public async Task<PagedResult<ProductOpeningBalanceDto>> Handle(
        ListProductOpeningBalancesQuery request, CancellationToken cancellationToken)
    {

        // Phase 32b. These two lists enumerate MASTER records (accounts, products) with the
        // opening line LEFT-joined on, so a location restriction narrows the joined figures rather
        // than hiding rows: a caller scoped to one branch still sees the whole chart of accounts,
        // and sees opening balances only for their own locations. Filtering the outer row instead
        // would hide accounts that merely happen to have no opening balance at that location.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        var query =
            from product in db.Products
            join category in db.ProductCategories on product.CategoryId equals category.Id
            join line in db.OpeningStockLines.Where(
                    x => x.OrganizationId == request.OrganizationId && x.WarehouseId == request.WarehouseId
                         && (allowedLocations == null
                             || (x.LocationId != null && allowedLocations.Contains(x.LocationId.Value))))
                on product.Id equals line.ProductId into lines
            from line in lines.DefaultIfEmpty()
            where product.OrganizationId == request.OrganizationId && product.TrackInventory
            orderby product.Code
            select new ProductOpeningBalanceDto(
                product.Id, product.Code, product.Name, category.Name,
                line == null ? 0m : line.Quantity, line == null ? 0m : line.Rate,
                line == null ? 0m : line.Quantity * line.Rate,
                line == null ? (Guid?)null : line.Id);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
