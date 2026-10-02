using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListKitchenStations;

public sealed record KitchenStationProductDto(Guid Id, string Code, string Name);

public sealed record KitchenStationDto(
    Guid Id, string Name, bool IsActive, IReadOnlyList<KitchenStationProductDto> Products);

/// <summary>
/// Phase 64 -- the kitchen stations (the vendor's Print Profiles), each with the products whose tickets
/// go there. Every product with none goes to the Default station, which is not a row.
///
/// <para>Under <c>Pos.Settings.Manage</c>: a station is POS configuration, like the location settings and
/// payment-mode links beside it on the same screen.</para>
/// </summary>
public sealed record ListKitchenStationsQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<KitchenStationDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class ListKitchenStationsQueryValidator : AbstractValidator<ListKitchenStationsQuery>
{
    public ListKitchenStationsQueryValidator() => RuleFor(x => x.OrganizationId).NotEmpty();
}

public sealed class ListKitchenStationsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListKitchenStationsQuery, IReadOnlyList<KitchenStationDto>>
{
    public async Task<IReadOnlyList<KitchenStationDto>> Handle(
        ListKitchenStationsQuery request, CancellationToken cancellationToken) =>
        await KitchenStationReader.ReadAsync(db, request.OrganizationId, cancellationToken);
}

internal static class KitchenStationReader
{
    public static async Task<IReadOnlyList<KitchenStationDto>> ReadAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var stations = await db.KitchenStations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var products = await db.Products
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.KitchenStationId != null)
            .OrderBy(x => x.Name)
            .Select(x => new { StationId = x.KitchenStationId!.Value, x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        return [.. stations.Select(s => new KitchenStationDto(
            s.Id,
            s.Name,
            s.IsActive,
            [.. products.Where(p => p.StationId == s.Id).Select(p => new KitchenStationProductDto(p.Id, p.Code, p.Name))]))];
    }
}
