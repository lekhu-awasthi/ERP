using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetGeneralSettings;

public sealed class GetGeneralSettingsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetGeneralSettingsQuery, GeneralSettingsDto>
{
    public async Task<GeneralSettingsDto> Handle(
        GetGeneralSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Tenant settings not found.");

        return new GeneralSettingsDto(
            settings.SuggestSellingPriceMode,
            settings.ProductPriceBasis,
            settings.InventoryTrackingMode,
            settings.NegativeCashBalanceAction,
            settings.NegativeStockBalanceAction,
            settings.CreditLimitExceedsAction);
    }
}
