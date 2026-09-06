using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy.Queries.GetGeneralSettings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateGeneralSettings;

public sealed class UpdateGeneralSettingsCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateGeneralSettingsCommand, GeneralSettingsDto>
{
    public async Task<GeneralSettingsDto> Handle(
        UpdateGeneralSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        settings.UpdateSettings(
            request.SuggestSellingPriceMode,
            request.ProductPriceBasis,
            request.InventoryTrackingMode,
            request.NegativeCashBalanceAction,
            request.NegativeStockBalanceAction,
            request.CreditLimitExceedsAction);

        await db.SaveChangesAsync(cancellationToken);

        return new GeneralSettingsDto(
            settings.SuggestSellingPriceMode,
            settings.ProductPriceBasis,
            settings.InventoryTrackingMode,
            settings.NegativeCashBalanceAction,
            settings.NegativeStockBalanceAction,
            settings.CreditLimitExceedsAction);
    }
}
