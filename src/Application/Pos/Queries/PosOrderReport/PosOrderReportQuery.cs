using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.PosOrderReport;

/// <summary>
/// One restaurant order. <paramref name="OrderValue"/> is what its lines come to at their frozen rates,
/// before any rounding (phase 64's estimate). <paramref name="Billed"/> is what its bills not voided came
/// to, each rounded to the rupee (phase 65). <paramref name="ToBill"/> is what is still unbilled: the
/// order value less what the bills charged before rounding -- exact, because the last part of a line takes
/// what is left of it (phase 65 Decision E). So OrderValue = Billed - the bills' round-off + ToBill.
/// </summary>
public sealed record PosOrderReportRowDto(
    Guid Id,
    string Code,
    DateOnly Date,
    DateTimeOffset CreatedAt,
    PosOrderStatus Status,
    PosTab OrderType,
    Guid LocationId,
    string LocationName,
    string? AreaName,
    string? TableName,
    int Covers,
    string ContactName,
    string CreatedByName,
    decimal OrderValue,
    decimal Billed,
    decimal BilledRoundOff,
    decimal ToBill,
    IReadOnlyList<PosOrderInvoiceDto> Invoices,
    string? VoidReason,
    DateTimeOffset? SettledAt);

/// <summary>
/// The footer is over every order the filters keep (phase 16c): the count in each state, what their
/// bills came to, and what the open ones still have to bill.
/// </summary>
public sealed record PosOrderReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<PosOrderReportRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int OpenCount,
    int SettledCount,
    int VoidedCount,
    decimal Billed,
    decimal ToBill);

/// <summary>
/// Phase 66 -- the Order Report: a period's restaurant orders with what each was billed (Decision E). The
/// vendor's lists its open Sales Orders, billed or not, with the bill number; ours are
/// <see cref="PosOrder"/>s (phase 59 Decision E), each with every bill it has had -- a split order has
/// several -- and what is still to bill. A refund changes nothing here: the food was billed and returned
/// (phase 65 Decision C), and the refund is on the Day Report and the Payment Summary.
///
/// <para>The POS Orders list's key, scope and markers (<see cref="PermissionKeys.PosOrderView"/>,
/// Admin+Member, narrowed to where the caller may view invoices): the same orders, read for totals.</para>
/// </summary>
public sealed record PosOrderReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? LocationId = null,
    PosOrderStatus? Status = null,
    PosTab? OrderType = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PosOrderReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredQuery
{
    public string PermissionKey => PermissionKeys.PosOrderView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class PosOrderReportQueryValidator : AbstractValidator<PosOrderReportQuery>
{
    public PosOrderReportQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.");
        RuleFor(x => x.ToDate)
            .Must((request, to) => to.DayNumber - request.FromDate.DayNumber < PosReportPeriod.MaxDays)
            .WithMessage($"A POS report covers at most {PosReportPeriod.MaxDays} days.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.OrderType)
            .Must(x => x is null or PosTab.DineIn or PosTab.TakeAway or PosTab.Delivery)
            .WithMessage("An order is Dine In, Take Away or Delivery.");
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}

public sealed class PosOrderReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PosOrderReportQuery, PosOrderReportDto>
{
    public async Task<PosOrderReportDto> Handle(PosOrderReportQuery request, CancellationToken cancellationToken)
    {
        if (request.LocationId is { } locationId
            && !await db.BillingLocations.AnyAsync(
                x => x.Id == locationId && x.OrganizationId == request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException("Billing location not found.");
        }

        var query = db.PosOrders.Where(x => x.OrganizationId == request.OrganizationId
            && x.Date >= request.FromDate && x.Date <= request.ToDate);

        // The POS Orders list's scope (phase 64 Decision F): the branches the caller may view invoices at.
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, PermissionKeys.InvoiceView, cancellationToken);
        if (allowedLocations is not null)
        {
            query = query.Where(x => allowedLocations.Contains(x.BillingLocationId));
        }

        if (request.LocationId is { } onlyLocation)
        {
            query = query.Where(x => x.BillingLocationId == onlyLocation);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (request.OrderType is { } orderType)
        {
            query = query.Where(x => x.OrderType == orderType);
        }

        // ---- the footer, over the whole filtered set ----------------------------------------------
        var counts = await query
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var orderIds = query.Select(x => x.Id);
        var billedInvoices = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId
            && x.Status == InvoiceStatus.Approved && x.PosOrderId != null && orderIds.Contains(x.PosOrderId.Value));
        var billedLines = await (
                from line in db.InvoiceLines
                join invoice in billedInvoices on line.InvoiceId equals invoice.Id
                select line.Amount + line.ServiceChargeAmount + line.VatAmount)
            .SumAsync(cancellationToken);
        var billedRoundOff = await billedInvoices.SumAsync(x => x.RoundOff, cancellationToken);

        var openOrders = await LoadAsync(query.Where(x => x.Status == PosOrderStatus.Open), cancellationToken);
        var openRows = await PosOrderView.ReadManyAsync(db, request.OrganizationId, openOrders, cancellationToken);

        // ---- the page --------------------------------------------------------------------------
        static IOrderedQueryable<PosOrder> Order(IQueryable<PosOrder> source) =>
            source.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt);

        var totalCount = counts.Sum(x => x.Count);
        IReadOnlyList<PosOrder> pageOrders;
        int page, pageSize;
        if (request.ExportAll)
        {
            pageOrders = await LoadAsync(Order(query), cancellationToken);
            (page, pageSize) = (1, Math.Max(totalCount, 1));
        }
        else
        {
            var keys = await query.ToKeyPagedResultAsync(x => x.Id, Order, request.Page, request.PageSize, cancellationToken);
            var ids = keys.Items.Select(x => x.Id).ToList();
            var loaded = await LoadAsync(query.Where(x => ids.Contains(x.Id)), cancellationToken);
            pageOrders = [.. ids.Select(id => loaded.Single(x => x.Id == id))];
            (page, pageSize) = (keys.Page, keys.PageSize);
        }

        var rows = (await PosOrderView.ReadManyAsync(db, request.OrganizationId, pageOrders, cancellationToken))
            .Select(Row)
            .ToList();

        return new PosOrderReportDto(
            request.FromDate,
            request.ToDate,
            rows,
            page,
            pageSize,
            totalCount,
            counts.Where(x => x.Status == PosOrderStatus.Open).Sum(x => x.Count),
            counts.Where(x => x.Status == PosOrderStatus.Settled).Sum(x => x.Count),
            counts.Where(x => x.Status == PosOrderStatus.Voided).Sum(x => x.Count),
            billedLines + billedRoundOff,
            openRows.Select(Row).Sum(x => x.ToBill));
    }

    private static async Task<List<PosOrder>> LoadAsync(IQueryable<PosOrder> orders, CancellationToken cancellationToken) =>
        await orders
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tickets).ThenInclude(x => x.Lines)
            .ToListAsync(cancellationToken);

    /// <summary>What an order still has to bill, in money: its value less what its bills not voided charged
    /// before rounding. Phase 66's one copy, read by this report and the dashboard.</summary>
    internal static decimal ToBill(PosOrderDto order)
    {
        if (order.Status == PosOrderStatus.Voided)
        {
            return 0m;
        }

        var live = order.Invoices.Where(x => !x.IsVoided).ToList();
        return order.Total - (live.Sum(x => x.GrandTotal) - live.Sum(x => x.RoundOff));
    }

    private static PosOrderReportRowDto Row(PosOrderDto order)
    {
        var billedRoundOff = order.Invoices.Where(x => !x.IsVoided).Sum(x => x.RoundOff);

        return new PosOrderReportRowDto(
            order.Id,
            order.Code,
            order.Date,
            order.CreatedAt,
            order.Status,
            order.OrderType,
            order.LocationId,
            order.LocationName,
            order.AreaName,
            order.TableName,
            order.Covers,
            order.ContactName,
            order.CreatedByName,
            order.Total,
            order.Billed,
            billedRoundOff,
            ToBill(order),
            order.Invoices,
            order.VoidReason,
            order.SettledAt);
    }
}
