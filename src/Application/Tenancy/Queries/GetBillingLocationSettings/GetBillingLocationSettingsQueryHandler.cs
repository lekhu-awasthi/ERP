using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetBillingLocationSettings;

public sealed class GetBillingLocationSettingsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetBillingLocationSettingsQuery, BillingLocationSettingsDto>
{
    public async Task<BillingLocationSettingsDto> Handle(
        GetBillingLocationSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.LocationScopeMode, x.LocationWiseReportPermission })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        // Fails closed on a missing subscription row, the same way FeatureGateBehavior and both
        // earlier entitlement caps do.
        var multipleLocationsEnabled = await db.TenantSubscriptions
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => (bool?)x.MultipleLocationsEnabled)
            .SingleOrDefaultAsync(cancellationToken) == true;

        var bearingTypes = DocumentLocationScope.For(settings.LocationScopeMode)
            .Select(x => x.ToString())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new BillingLocationSettingsDto(
            settings.LocationScopeMode,
            settings.LocationWiseReportPermission,
            multipleLocationsEnabled,
            bearingTypes);
    }
}
