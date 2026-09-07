using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListBillingLocations;

public sealed class ListBillingLocationsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListBillingLocationsQuery, IReadOnlyList<BillingLocationDto>>
{
    public async Task<IReadOnlyList<BillingLocationDto>> Handle(
        ListBillingLocationsQuery request, CancellationToken cancellationToken)
    {
        var rows = await (
            from location in db.BillingLocations
            where location.OrganizationId == request.OrganizationId
                  && (request.IncludeInactive || location.IsActive)
            join warehouse in db.Warehouses on location.WarehouseId equals warehouse.Id into warehouses
            from warehouse in warehouses.DefaultIfEmpty()
            orderby location.LocationType, location.Code
            select new BillingLocationDto(
                location.Id,
                location.Code,
                location.Name,
                location.Address,
                location.WarehouseId,
                warehouse != null ? warehouse.Name : null,
                location.LocationType,
                location.LocationType == BillingLocationType.HeadOffice,
                location.IsActive))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
