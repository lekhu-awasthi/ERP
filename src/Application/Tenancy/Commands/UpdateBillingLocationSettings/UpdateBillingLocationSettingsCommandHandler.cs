using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy.Queries.GetBillingLocationSettings;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateBillingLocationSettings;

public sealed class UpdateBillingLocationSettingsCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateBillingLocationSettingsCommand, BillingLocationSettingsDto>
{
    public async Task<BillingLocationSettingsDto> Handle(
        UpdateBillingLocationSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        var multipleLocationsEnabled = await db.TenantSubscriptions
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => (bool?)x.MultipleLocationsEnabled)
            .SingleOrDefaultAsync(cancellationToken) == true;

        // The Advanced panel only exists on a tenant that has the entitlement -- live, a tenant
        // without it sees "Billing Location: Disabled / reach out to Tigg Support" and no panel at
        // all. Widening the scope without it would be meaningless anyway: with one location the
        // setting cannot change any document's outcome. This is the *panel* being gated, not the
        // documents (see CreateBillingLocationCommandHandler for why no document command is gated).
        if (!multipleLocationsEnabled)
        {
            throw new FeatureNotEnabledException(
                "This organization does not have the Multiple Locations feature enabled, so how locations "
                + "are used across its documents is not configurable. Accounting Features are chosen when the "
                + "organization is created and cannot be changed afterwards.");
        }

        settings.SetLocationSettings(request.LocationScopeMode, request.LocationWiseReportPermission);

        await db.SaveChangesAsync(cancellationToken);

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
