using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.GetStockTrackingSwitches;

/// <summary>
/// Phase 95 -- the company's two tracking switches, for the forms that decide whether a line shows a batch or serial box
/// and the product form that hides a product's own switch while the company's is off. Under
/// <c>Catalog.Product.View</c>, which every such form already needs for its product list, rather than
/// <c>Configurations.GeneralSettings.Manage</c>: a clerk entering a return must not need the settings screen's key to
/// see which boxes to fill.
/// </summary>
public sealed record GetStockTrackingSwitchesQuery(Guid OrganizationId)
    : IRequest<StockTrackingSwitchesDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}

public sealed record StockTrackingSwitchesDto(bool BatchTrackingEnabled, bool SerialTrackingEnabled);

public sealed class GetStockTrackingSwitchesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetStockTrackingSwitchesQuery, StockTrackingSwitchesDto>
{
    public async Task<StockTrackingSwitchesDto> Handle(
        GetStockTrackingSwitchesQuery request, CancellationToken cancellationToken)
    {
        var switches = await StockTrackingSwitches.LoadAsync(db, request.OrganizationId, cancellationToken);
        return new StockTrackingSwitchesDto(switches.Batch, switches.Serial);
    }
}
