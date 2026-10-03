using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Queries.PosOrderReport;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Application.Trade;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosDashboard;

/// <summary>One product's takings over the period, net of refunds: net sales, service charge and VAT.</summary>
public sealed record PosDashboardProductDto(Guid ProductId, string Code, string Name, string? Unit, decimal Quantity, decimal Total);

/// <summary>A drawer open right now.</summary>
public sealed record PosDashboardSessionDto(
    Guid Id, string Code, Guid LocationId, string LocationName, string UserName, DateTimeOffset OpenedAt);

/// <summary>
/// Phase 66 -- the POS home's overview. Every figure is read from a reader a report also reads, so the
/// dashboard and the reports it links to cannot disagree (the vendor's dashboard said 611 above a products
/// panel totalling 610.20, and Cash 417 against its Day Report's 349):
///
/// <list type="bullet">
/// <item><paramref name="Sales"/>, <paramref name="Payments"/> and <paramref name="Series"/> are the Day
/// Report's (<see cref="PosSalesReader.ForPeriodAsync"/>);</item>
/// <item><paramref name="TopProducts"/> and <paramref name="OtherProducts"/> are Sales by Item with
/// Channel = POS (<see cref="TradeLineReader"/>), and with <see cref="PosSalesSummaryDto.NetRoundOff"/> they
/// add up to <see cref="PosSalesSummaryDto.NetSales"/> -- the panel prints the round-off as its own row
/// rather than clamping a difference away;</item>
/// <item><paramref name="OpenOrders"/> and <paramref name="OpenOrdersToBill"/> are the Order Report's rule
/// over the orders open now.</item>
/// </list>
/// </summary>
public sealed record PosDashboardDto(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? LocationId,
    PosSalesSummaryDto Sales,
    PosPaymentBreakdownDto Payments,
    PosSalesBucket Bucket,
    IReadOnlyList<PosSalesPointDto> Series,
    IReadOnlyList<PosDashboardProductDto> TopProducts,
    decimal OtherProducts,
    int OpenOrders,
    decimal OpenOrdersToBill,
    IReadOnlyList<PosDashboardSessionDto> OpenSessions);

/// <summary>
/// Phase 66 -- the dashboard. <see cref="PermissionKeys.PosSessionViewAll"/>, the Day Report's key: it is
/// the Day Report as a picture, every cashier's takings, plus who is on a drawer now (Decision G).
/// </summary>
public sealed record GetPosDashboardQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate, Guid? LocationId = null)
    : IRequest<PosDashboardDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    /// <summary>How many products the panel names before it groups the rest as "Other products".</summary>
    public const int TopProductCount = 5;

    public string PermissionKey => PermissionKeys.PosSessionViewAll;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class GetPosDashboardQueryValidator : AbstractValidator<GetPosDashboardQuery>
{
    public GetPosDashboardQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.");
        RuleFor(x => x.ToDate)
            .Must((request, to) => to.DayNumber - request.FromDate.DayNumber < PosReportPeriod.MaxDays)
            .WithMessage($"A POS report covers at most {PosReportPeriod.MaxDays} days.");
    }
}

public sealed class GetPosDashboardQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPosDashboardQuery, PosDashboardDto>
{
    public async Task<PosDashboardDto> Handle(GetPosDashboardQuery request, CancellationToken cancellationToken)
    {
        if (request.LocationId is { } locationId
            && !await db.BillingLocations.AnyAsync(
                x => x.Id == locationId && x.OrganizationId == request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException("Billing location not found.");
        }

        var period = await PosSalesReader.ForPeriodAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.LocationId, cancellationToken);

        // ---- products: Sales by Item's facts, Channel = POS -------------------------------------
        var facts = await TradeLineReader.LoadAsync(
            db, request.OrganizationId, TradeSide.Sales, request.FromDate, request.ToDate, cancellationToken,
            request.LocationId, reportLocations: null, channel: SalesChannel.Pos);

        var byProduct = facts
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity), Total = g.Sum(x => x.TotalAmount) })
            .Where(x => x.Quantity != 0 || x.Total != 0)
            .ToList();

        var productIds = byProduct.Select(x => x.ProductId).ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.PrimaryUnitId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var units = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == request.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var ranked = byProduct
            .Select(x =>
            {
                var product = products.GetValueOrDefault(x.ProductId);
                return new PosDashboardProductDto(
                    x.ProductId, product?.Code ?? "", product?.Name ?? "",
                    product is null ? null : units.GetValueOrDefault(product.PrimaryUnitId),
                    x.Quantity, x.Total);
            })
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        var top = ranked.Take(GetPosDashboardQuery.TopProductCount).ToList();
        var other = ranked.Skip(GetPosDashboardQuery.TopProductCount).Sum(x => x.Total);

        // ---- open orders and open drawers, now ----------------------------------------------------
        var openOrdersQuery = db.PosOrders.Where(x => x.OrganizationId == request.OrganizationId
            && x.Status == PosOrderStatus.Open);
        var openSessionsQuery = db.PosSessions.Where(x => x.OrganizationId == request.OrganizationId
            && x.Status == PosSessionStatus.Open);
        if (request.LocationId is { } onlyLocation)
        {
            openOrdersQuery = openOrdersQuery.Where(x => x.BillingLocationId == onlyLocation);
            openSessionsQuery = openSessionsQuery.Where(x => x.BillingLocationId == onlyLocation);
        }

        var openOrders = await openOrdersQuery
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tickets).ThenInclude(x => x.Lines)
            .ToListAsync(cancellationToken);
        var openOrderRows = await PosOrderView.ReadManyAsync(db, request.OrganizationId, openOrders, cancellationToken);

        var openSessions = await (
                from session in openSessionsQuery
                join user in db.Users on session.UserId equals user.Id
                join location in db.BillingLocations on session.BillingLocationId equals location.Id
                orderby session.OpenedAt
                select new PosDashboardSessionDto(
                    session.Id, session.Code, session.BillingLocationId, location.Name, user.FullName, session.OpenedAt))
            .ToListAsync(cancellationToken);

        return new PosDashboardDto(
            request.FromDate,
            request.ToDate,
            request.LocationId,
            period.Summary,
            PosSalesReader.Payments(period.Summary),
            period.Bucket,
            period.Series,
            top,
            other,
            openOrderRows.Count,
            openOrderRows.Sum(PosOrderReportQueryHandler.ToBill),
            openSessions);
    }
}
