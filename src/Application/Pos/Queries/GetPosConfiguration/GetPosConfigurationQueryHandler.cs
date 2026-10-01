using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosConfiguration;

public sealed class GetPosConfigurationQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPosConfigurationQuery, PosConfigurationDto>
{
    public async Task<PosConfigurationDto> Handle(GetPosConfigurationQuery request, CancellationToken cancellationToken)
    {
        var features = await db.TenantSubscriptions
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.PosRetailEnabled, x.PosRestaurantEnabled })
            .SingleOrDefaultAsync(cancellationToken);

        var walkIn = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && x.IsWalkInCustomer)
            .Select(x => new PosWalkInCustomerDto(x.Id, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken);

        var locations = await db.BillingLocations
            .Where(x => x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.LocationType)
            .ThenBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name, x.IsActive, x.LocationType, x.PosMode })
            .ToListAsync(cancellationToken);

        var saved = await db.PosLocationSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => x.BillingLocationId)
            .ToListAsync(cancellationToken);

        var linkCounts = await db.PosLocationPaymentModes
            .Where(x => x.OrganizationId == request.OrganizationId)
            .GroupBy(x => x.BillingLocationId)
            .Select(g => new { LocationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LocationId, x => x.Count, cancellationToken);

        return new PosConfigurationDto(
            features?.PosRetailEnabled == true,
            features?.PosRestaurantEnabled == true,
            walkIn,
            locations
                .Select(x => new PosLocationSummaryDto(
                    x.Id,
                    x.Code,
                    x.Name,
                    x.IsActive,
                    x.LocationType == BillingLocationType.HeadOffice,
                    x.PosMode,
                    saved.Contains(x.Id),
                    linkCounts.GetValueOrDefault(x.Id)))
                .ToList());
    }
}
