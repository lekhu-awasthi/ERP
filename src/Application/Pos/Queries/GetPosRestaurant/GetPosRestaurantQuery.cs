using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Application.Pos.Queries.GetPosTill;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosRestaurant;

/// <summary>An open order as the floor and the Take Away / Delivery lists show it.</summary>
/// <param name="Outstanding">Items still to come from the kitchen; zero means everything was served.</param>
public sealed record PosOpenOrderDto(
    Guid Id,
    string Code,
    PosTab OrderType,
    Guid? TableId,
    int Covers,
    string ContactName,
    DateTimeOffset CreatedAt,
    decimal Outstanding,
    decimal Total);

/// <param name="Order">The open order seated here; null when the table is free.</param>
public sealed record PosRestaurantTableDto(
    Guid Id, string Name, int Capacity, PosTableShape Shape, int X, int Y, int Width, int Height, PosOpenOrderDto? Order);

public sealed record PosRestaurantAreaDto(Guid Id, string Name, IReadOnlyList<PosRestaurantTableDto> Tables);

/// <param name="Orders">Every open order here: the Dine In ones are also on their tables.</param>
/// <param name="CanVoid">Whether the caller holds <c>Pos.Order.Void</c>, so the order screen can say who
/// may discard before a waiter tries (phase 62's say-it-before-Pay rule).</param>
public sealed record PosRestaurantDto(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    IReadOnlyList<PosTab> AvailableTabs,
    PosTab? DefaultTab,
    bool ServiceChargeEnabled,
    decimal ServiceChargeRate,
    bool PrintKot,
    int CanvasWidth,
    int CanvasHeight,
    PosWalkInCustomerDto? WalkInCustomer,
    IReadOnlyList<PosTillCategoryDto> Categories,
    IReadOnlyList<PosRestaurantAreaDto> Areas,
    IReadOnlyList<PosOpenOrderDto> Orders,
    bool CanVoid);

/// <summary>
/// Phase 64 -- the restaurant till's own read of its location: the floor with which tables are taken,
/// the open Take Away and Delivery orders, and what the order screen needs (service charge, KOT
/// printing, categories, the walk-in). Under <c>Pos.Order.Operate</c>, because a waiter is not a cashier
/// (phase 62 Decision D's reasoning, one role over).
///
/// <para><b>Occupancy is read, not stored</b>: a table is taken exactly when an open order names it.</para>
/// </summary>
public sealed record GetPosRestaurantQuery(Guid OrganizationId, Guid LocationId)
    : IRequest<PosRestaurantDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class GetPosRestaurantQueryValidator : AbstractValidator<GetPosRestaurantQuery>
{
    public GetPosRestaurantQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
    }
}

public sealed class GetPosRestaurantQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPosRestaurantQuery, PosRestaurantDto>
{
    public async Task<PosRestaurantDto> Handle(GetPosRestaurantQuery request, CancellationToken cancellationToken)
    {
        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        var location = till.Location;
        await PosRestaurant.EnsureMayOrderAtAsync(db, request.OrganizationId, currentUser.UserId, location.Id, cancellationToken);

        var areas = await db.PosAreas
            .AsNoTracking()
            .Include(x => x.Tables)
            .Where(x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == location.Id && x.IsActive)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var openOrders = await db.PosOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tickets).ThenInclude(x => x.Lines)
            .Where(x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == location.Id
                && x.Status == PosOrderStatus.Open)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var views = await PosOrderView.ReadManyAsync(db, request.OrganizationId, openOrders, cancellationToken);
        var orders = views
            .Select(v => new PosOpenOrderDto(
                v.Id, v.Code, v.OrderType, v.TableId, v.Covers, v.ContactName, v.CreatedAt, v.Outstanding, v.Total))
            .ToList();
        var byTable = orders.Where(x => x.TableId != null).ToDictionary(x => x.TableId!.Value);

        var walkIn = await db.Contacts
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.IsWalkInCustomer)
            .Select(x => new PosWalkInCustomerDto(x.Id, x.Code, x.Name))
            .SingleOrDefaultAsync(cancellationToken);

        // The grid's tabs: GetPosTillQueryHandler's own rule, so a tab is never empty.
        var categories = await db.ProductCategories
            .AsNoTracking()
            .Where(c => c.OrganizationId == request.OrganizationId)
            .Where(c => db.Products.Any(p =>
                p.OrganizationId == request.OrganizationId && p.CategoryId == c.Id
                && p.AvailableForSale && p.IsActive && !p.HasVariants
                && (p.Locations.Count == 0 || p.Locations.Any(l => l.LocationId == location.Id))))
            .OrderBy(c => c.Name)
            .Select(c => new PosTillCategoryDto(c.Id, c.Name))
            .ToListAsync(cancellationToken);

        var canVoid = await GrantedPermissionReader.IsGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.PosOrderVoid, location.Id, cancellationToken);

        return new PosRestaurantDto(
            location.Id,
            location.Code,
            location.Name,
            PosTabs.For(location.PosMode),
            till.Settings.EffectiveDefaultTab(location.PosMode),
            till.Settings.ServiceChargeEnabled,
            till.Settings.ServiceChargeRate,
            till.Settings.PrintKot,
            PosArea.CanvasWidth,
            PosArea.CanvasHeight,
            walkIn,
            categories,
            [.. areas.Select(a => new PosRestaurantAreaDto(
                a.Id,
                a.Name,
                [.. a.Tables
                    .Where(t => t.IsActive)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => new PosRestaurantTableDto(
                        t.Id, t.Name, t.Capacity, t.Shape, t.X, t.Y, t.Width, t.Height, byTable.GetValueOrDefault(t.Id)))]))],
            orders,
            canVoid);
    }
}
