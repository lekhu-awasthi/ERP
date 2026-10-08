using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosLocationSettings;

public sealed class GetPosLocationSettingsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPosLocationSettingsQuery, PosLocationSettingsDto>
{
    public async Task<PosLocationSettingsDto> Handle(
        GetPosLocationSettingsQuery request, CancellationToken cancellationToken)
    {
        var location = await db.BillingLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Billing location not found.");

        return await PosLocationSettingsReader.ReadAsync(db, location, cancellationToken);
    }
}

/// <summary>
/// Phase 60 -- builds the settings view of one location, shared by the query and by both commands
/// that return it, so a save answers with exactly what a reload would show.
/// </summary>
internal static class PosLocationSettingsReader
{
    public static async Task<PosLocationSettingsDto> ReadAsync(
        IAppDbContext db, BillingLocation location, CancellationToken cancellationToken)
    {
        var stored = await db.PosLocationSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == location.OrganizationId && x.BillingLocationId == location.Id,
                cancellationToken);

        var settings = stored ?? PosLocationSettings.CreateDefault(location.OrganizationId, location.Id);

        var paymentModeIds = await db.PosLocationPaymentModes
            .Where(x => x.OrganizationId == location.OrganizationId && x.BillingLocationId == location.Id)
            .Select(x => x.PaymentModeId)
            .ToListAsync(cancellationToken);

        return new PosLocationSettingsDto(
            location.Id,
            location.Code,
            location.Name,
            location.IsActive,
            location.PosMode,
            stored is not null,
            settings.ServiceChargeEnabled,
            settings.ServiceChargeRate,
            settings.ServiceChargeAccountId,
            settings.ServiceChargeOnTakeAway,
            settings.RoundOffEnabled,
            settings.RoundOffAccountId,
            settings.CashVerificationRequired,
            settings.Denominations,
            settings.DefaultTab,
            settings.EffectiveDefaultTab(location.PosMode),
            PosTabs.For(location.PosMode),
            settings.PrintEstimateBill,
            settings.PrintInvoice,
            settings.PrintCreditNote,
            settings.PrintKot,
            settings.AbbreviatedTaxInvoiceEnabled,
            paymentModeIds);
    }
}
