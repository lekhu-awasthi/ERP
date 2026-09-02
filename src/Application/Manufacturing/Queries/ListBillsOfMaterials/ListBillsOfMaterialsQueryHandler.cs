using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.ListBillsOfMaterials;

public sealed class ListBillsOfMaterialsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListBillsOfMaterialsQuery, PagedResult<BillOfMaterialsListItemDto>>
{
    public async Task<PagedResult<BillOfMaterialsListItemDto>> Handle(
        ListBillsOfMaterialsQuery request, CancellationToken cancellationToken)
    {
        // String.Contains rather than EF.Functions.Like: the InMemory provider cannot translate
        // Like at all, while Contains becomes the same LIKE '%term%' on SQL Server.
        var query =
            from bom in db.BillsOfMaterials
            join product in db.Products on bom.ProductId equals product.Id
            join unit in db.UnitsOfMeasurement on product.PrimaryUnitId equals unit.Id into units
            from unit in units.DefaultIfEmpty()
            where bom.OrganizationId == request.OrganizationId
                && (request.IsActive == null || bom.IsActive == request.IsActive)
                && (string.IsNullOrWhiteSpace(request.Search)
                    || product.Name.Contains(request.Search!)
                    || product.Code.Contains(request.Search!))
            orderby product.Name
            select new BillOfMaterialsListItemDto(
                bom.Id,
                bom.ProductId,
                product.Name,
                product.Code,
                unit != null ? unit.Name : null,
                bom.OutputQuantity,
                bom.RawMaterials.Count,
                bom.ByProducts.Count,
                bom.ManufactureOnEverySale,
                bom.IsActive);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
