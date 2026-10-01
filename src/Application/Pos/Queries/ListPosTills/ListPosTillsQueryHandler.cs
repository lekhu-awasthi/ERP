using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosTills;

public sealed class ListPosTillsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListPosTillsQuery, IReadOnlyList<PosTillSummaryDto>>
{
    public async Task<IReadOnlyList<PosTillSummaryDto>> Handle(
        ListPosTillsQuery request, CancellationToken cancellationToken)
    {
        // The entitlements, read once: a location whose mode the tenant no longer holds is a till
        // nobody can open (PosTill.LoadAsync refuses it), so it is not offered.
        var subscription = await db.TenantSubscriptions.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken);

        var entitledModes = new List<PosMode>(2);
        if (subscription?.IsEnabled(TenantFeature.PosRetail) == true)
        {
            entitledModes.Add(PosMode.Retail);
        }

        if (subscription?.IsEnabled(TenantFeature.PosRestaurant) == true)
        {
            entitledModes.Add(PosMode.Restaurant);
        }

        var locations = await db.BillingLocations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.IsActive && entitledModes.Contains(x.PosMode))
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name, x.PosMode })
            .ToListAsync(cancellationToken);

        // Opening a drawer needs Sales.Invoice.Create at the location (phase 61 Decision F), so the
        // list is the locations where that holds: everywhere for an organization-wide grant, else
        // only the branches granted it. Two reads, not one per location.
        var organizationWide = (await GrantedPermissionReader.GrantedKeysAsync(
                db, request.OrganizationId, currentUser.UserId, cancellationToken))
            .Contains(PermissionKeys.InvoiceCreate);

        if (!organizationWide)
        {
            var granted = await GrantedPermissionReader.GrantedLocationsAsync(
                db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceCreate, cancellationToken);

            locations = locations.Where(x => granted.Contains(x.Id)).ToList();
        }

        var sessions = await db.PosSessions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.UserId == currentUser.UserId
                && x.Status == PosSessionStatus.Open)
            .Select(x => new { x.BillingLocationId, x.Id, x.Code, x.OpenedAt })
            .ToListAsync(cancellationToken);

        return locations
            .Select(x =>
            {
                var session = sessions.SingleOrDefault(s => s.BillingLocationId == x.Id);
                return new PosTillSummaryDto(
                    x.Id, x.Code, x.Name, x.PosMode, session?.Id, session?.Code, session?.OpenedAt);
            })
            .ToList();
    }
}
