using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.SetLocationPosMode;

public sealed class SetLocationPosModeCommandHandler(IAppDbContext db)
    : IRequestHandler<SetLocationPosModeCommand, PosLocationSettingsDto>
{
    public async Task<PosLocationSettingsDto> Handle(SetLocationPosModeCommand request, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        if (request.PosMode != PosMode.None)
        {
            var feature = request.PosMode == PosMode.Retail ? TenantFeature.PosRetail : TenantFeature.PosRestaurant;

            // Fails closed on a missing subscription row, like FeatureGateBehavior.
            var subscription = await db.TenantSubscriptions.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId, cancellationToken);

            if (subscription is null || !subscription.IsEnabled(feature))
            {
                var name = request.PosMode == PosMode.Retail ? "Point of Sale (Retail)" : "Point of Sale (Restaurant)";
                throw new FeatureNotEnabledException(
                    $"This organization does not have the {name} feature enabled, so no location can run a "
                    + $"{request.PosMode} till. Accounting Features are chosen when the organization is created and "
                    + "cannot be changed afterwards.");
            }
        }

        try
        {
            location.SetPosMode(request.PosMode);
        }
        catch (InvalidOperationException ex)
        {
            // An inactive location given a till. A 409 naming the reason, not the Domain's 500.
            throw new ConflictException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await PosLocationSettingsReader.ReadAsync(db, location, cancellationToken);
    }
}
