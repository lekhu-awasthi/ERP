using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;

public sealed class ListInventoryAdjustmentsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListInventoryAdjustmentsQuery, PagedResult<InventoryAdjustment>>
{
    public async Task<PagedResult<InventoryAdjustment>> Handle(
        ListInventoryAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.InventoryAdjustments.Where(x => x.OrganizationId == request.OrganizationId);

        // Phase 32b -- a caller granted this key only at certain billing locations sees only
        // those locations' rows, rather than being refused the list outright. Null (and so no
        // filter at all) for everyone holding the key organization-wide, which is every caller
        // on every tenant that has not opened the Location-specific permission section.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, request.PermissionKey, cancellationToken);

        if (allowedLocations is not null)
        {
            query = query.Where(x => x.LocationId != null && allowedLocations.Contains(x.LocationId.Value));
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
