using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosFloorPlan;

/// <param name="IsOccupied">An open order is seated here, so the table cannot be made inactive.</param>
public sealed record PosFloorTableDto(
    Guid Id, string Name, int Capacity, PosTableShape Shape, int X, int Y, int Width, int Height, bool IsActive,
    bool IsOccupied);

public sealed record PosFloorAreaDto(Guid Id, string Name, bool IsActive, IReadOnlyList<PosFloorTableDto> Tables);

/// <param name="CanvasWidth">The fixed canvas every area is laid out on (<see cref="PosArea"/>).</param>
public sealed record PosFloorPlanDto(
    Guid LocationId,
    string LocationName,
    int CanvasWidth,
    int CanvasHeight,
    int MaxCapacity,
    IReadOnlyList<PosFloorAreaDto> Areas);

/// <summary>
/// Phase 64 -- one Restaurant location's floor as its editor shows it: every area and table, active or
/// not (the vendor's Layout menu offers <i>View Inactive</i>), with which tables are occupied now.
/// </summary>
public sealed record GetPosFloorPlanQuery(Guid OrganizationId, Guid LocationId)
    : IRequest<PosFloorPlanDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosFloorPlanManage;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class GetPosFloorPlanQueryValidator : AbstractValidator<GetPosFloorPlanQuery>
{
    public GetPosFloorPlanQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
    }
}

public sealed class GetPosFloorPlanQueryHandler(IAppDbContext db) : IRequestHandler<GetPosFloorPlanQuery, PosFloorPlanDto>
{
    public async Task<PosFloorPlanDto> Handle(GetPosFloorPlanQuery request, CancellationToken cancellationToken)
    {
        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        return await PosFloorPlanReader.ReadAsync(db, request.OrganizationId, till.Location, cancellationToken);
    }
}

internal static class PosFloorPlanReader
{
    public static async Task<PosFloorPlanDto> ReadAsync(
        IAppDbContext db, Guid organizationId, BillingLocation location, CancellationToken cancellationToken)
    {
        var areas = await db.PosAreas
            .AsNoTracking()
            .Include(x => x.Tables)
            .Where(x => x.OrganizationId == organizationId && x.BillingLocationId == location.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var occupied = (await db.PosOrders
                .Where(x => x.OrganizationId == organizationId && x.BillingLocationId == location.Id
                    && x.Status == PosOrderStatus.Open && x.PosTableId != null)
                .Select(x => x.PosTableId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return new PosFloorPlanDto(
            location.Id,
            location.Name,
            PosArea.CanvasWidth,
            PosArea.CanvasHeight,
            PosTable.MaxCapacity,
            [.. areas.Select(a => new PosFloorAreaDto(
                a.Id,
                a.Name,
                a.IsActive,
                [.. a.Tables
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => new PosFloorTableDto(
                        t.Id, t.Name, t.Capacity, t.Shape, t.X, t.Y, t.Width, t.Height, t.IsActive,
                        occupied.Contains(t.Id)))]))]);
    }
}
