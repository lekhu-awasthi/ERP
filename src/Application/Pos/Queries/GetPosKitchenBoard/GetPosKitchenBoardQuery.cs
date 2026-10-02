using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosKitchenBoard;

/// <summary>Which tickets the board shows: the vendor's three tabs.</summary>
public enum PosKitchenBoardView
{
    /// <summary>Sends with something still to cook, oldest first, however old.</summary>
    Pending = 1,

    /// <summary>Today's sends that are done (served, or cancelled before any was), newest first.</summary>
    Served = 2,

    /// <summary>Today's tickets of every kind, cancellations included, newest first.</summary>
    All = 3,
}

public sealed record PosKitchenBoardLineDto(
    Guid OrderLineId,
    string ProductName,
    string? UnitName,
    string? Note,
    decimal Sent,
    decimal Served,
    decimal Cancelled,
    decimal Pending);

/// <param name="Number">The paper's number: the order's code and the send, <c>ORD0007-2</c>.</param>
/// <param name="Label">What the card is headed with: the table, or the customer of a Take Away or Delivery.</param>
public sealed record PosKitchenBoardTicketDto(
    Guid Id,
    string Number,
    Guid OrderId,
    string OrderCode,
    PosTab OrderType,
    PosOrderStatus OrderStatus,
    string Label,
    string? AreaName,
    int Covers,
    Guid? KitchenStationId,
    string KitchenStationName,
    KitchenTicketState State,
    string? Reason,
    DateTimeOffset CreatedAt,
    string CreatedByName,
    IReadOnlyList<PosKitchenBoardLineDto> Lines);

/// <summary>The vendor's Order Summary: what is still to cook, summed across the pending tickets shown.</summary>
public sealed record PosKitchenSummaryDto(string ProductName, string? UnitName, decimal Pending);

public sealed record PosKitchenStationOptionDto(Guid? Id, string Name);

/// <param name="Version">Changes whenever anything on the board could have: the latest activity on any
/// order it reads. The board polls, and announces new tickets only when this moves.</param>
public sealed record PosKitchenBoardDto(
    Guid LocationId,
    string LocationName,
    IReadOnlyList<PosKitchenStationOptionDto> Stations,
    int PendingCount,
    IReadOnlyList<PosKitchenBoardTicketDto> Tickets,
    IReadOnlyList<PosKitchenSummaryDto> Summary,
    DateTimeOffset? Version,
    DateTimeOffset ReadAt);

/// <summary>
/// Phase 65 -- a location's kitchen board: one card per kitchen ticket, each line showing what was sent,
/// served, cancelled and is still to cook (<see cref="PosOrder.TicketProgress"/>), filtered by station
/// and order type. Under <c>Pos.Kitchen.Operate</c> (Decision G).
///
/// <para><b>Polled, not pushed</b> (Decision F): the screen asks every ten seconds while it is visible,
/// and on Refresh. The vendor's board has only the button.</para>
///
/// <para><b>What it reads</b>: open orders, and orders that changed in the last day -- a Take Away is
/// paid before it is cooked, so a settled order's tickets stay until served. Pending shows every pending
/// send among them, oldest first; Served and All show today's (the Nepal day), newest first, at most
/// <see cref="MaxDoneTickets"/>.</para>
/// </summary>
public sealed record GetPosKitchenBoardQuery(
    Guid OrganizationId,
    Guid LocationId,
    PosKitchenBoardView View = PosKitchenBoardView.Pending,
    Guid? StationId = null,
    bool DefaultStation = false,
    PosTab? OrderType = null)
    : IRequest<PosKitchenBoardDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public const int MaxDoneTickets = 200;

    public string PermissionKey => PermissionKeys.PosKitchenOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class GetPosKitchenBoardQueryValidator : AbstractValidator<GetPosKitchenBoardQuery>
{
    public GetPosKitchenBoardQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.View).IsInEnum();
        RuleFor(x => x.OrderType)
            .Must(x => x is null or PosTab.DineIn or PosTab.TakeAway or PosTab.Delivery)
            .WithMessage("An order type is Dine In, Take Away or Delivery.");
        RuleFor(x => x.StationId)
            .Null()
            .When(x => x.DefaultStation)
            .WithMessage("Choose one station: a named one, or Default.");
    }
}

public sealed class GetPosKitchenBoardQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPosKitchenBoardQuery, PosKitchenBoardDto>
{
    public async Task<PosKitchenBoardDto> Handle(GetPosKitchenBoardQuery request, CancellationToken cancellationToken)
    {
        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-1);

        var orders = await db.PosOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tickets).ThenInclude(x => x.Lines)
            .Where(x => x.OrganizationId == request.OrganizationId && x.BillingLocationId == till.Location.Id
                && (x.Status == PosOrderStatus.Open || x.LastActivityAt >= since))
            .ToListAsync(cancellationToken);

        var stations = await db.KitchenStations
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var stationNames = stations.ToDictionary(x => x.Id, x => x.Name);

        var view = await PosOrderView.ReadManyAsync(db, request.OrganizationId, orders, cancellationToken);
        var views = view.ToDictionary(x => x.Id);

        var today = NepalTime.LocalDate(now);
        var all = orders
            .Where(o => request.OrderType is null || o.OrderType == request.OrderType)
            .SelectMany(o => o.TicketProgress().Select(p => (Order: o, Progress: p, Ticket: o.Tickets.Single(t => t.Id == p.TicketId))))
            .Where(x => !request.DefaultStation || x.Ticket.KitchenStationId is null)
            .Where(x => request.StationId is null || x.Ticket.KitchenStationId == request.StationId)
            .ToList();

        var pending = all.Where(x => x.Progress.State == KitchenTicketState.Pending).ToList();

        var shown = request.View switch
        {
            PosKitchenBoardView.Pending => pending.OrderBy(x => x.Ticket.CreatedAt).ToList(),
            PosKitchenBoardView.Served => all
                .Where(x => x.Progress.State is KitchenTicketState.Served or KitchenTicketState.Cancelled
                    && NepalTime.LocalDate(x.Ticket.CreatedAt) == today)
                .OrderByDescending(x => x.Ticket.CreatedAt).Take(GetPosKitchenBoardQuery.MaxDoneTickets).ToList(),
            _ => all
                .Where(x => NepalTime.LocalDate(x.Ticket.CreatedAt) == today || x.Progress.State == KitchenTicketState.Pending)
                .OrderByDescending(x => x.Ticket.CreatedAt).Take(GetPosKitchenBoardQuery.MaxDoneTickets).ToList(),
        };

        var tickets = shown
            .Select(x =>
            {
                var order = views[x.Order.Id];
                var lines = order.Lines.ToDictionary(l => l.Id);
                var label = order.TableName ?? order.ContactName;

                return new PosKitchenBoardTicketDto(
                    x.Ticket.Id,
                    $"{order.Code}-{x.Ticket.SendNumber}",
                    order.Id,
                    order.Code,
                    order.OrderType,
                    order.Status,
                    label,
                    order.AreaName,
                    order.Covers,
                    x.Ticket.KitchenStationId,
                    x.Ticket.KitchenStationId is { } s ? stationNames.GetValueOrDefault(s, "") : KitchenStation.DefaultName,
                    x.Progress.State,
                    x.Ticket.Reason,
                    x.Ticket.CreatedAt,
                    order.Tickets.Single(t => t.Id == x.Ticket.Id).CreatedByName,
                    [.. x.Progress.Lines
                        .Select(l => (Progress: l, Line: lines[l.OrderLineId]))
                        .OrderBy(l => l.Line.LineNo)
                        .Select(l => new PosKitchenBoardLineDto(
                            l.Line.Id, l.Line.ProductName, l.Line.UnitName, l.Line.Note,
                            l.Progress.Sent, l.Progress.Served, l.Progress.Cancelled, l.Progress.Pending))]);
            })
            .ToList();

        var summary = pending
            .SelectMany(x => x.Progress.Lines
                .Where(l => l.Pending > 0m)
                .Select(l => (Line: views[x.Order.Id].Lines.Single(v => v.Id == l.OrderLineId), l.Pending)))
            .GroupBy(x => (x.Line.ProductName, x.Line.UnitName))
            .Select(g => new PosKitchenSummaryDto(g.Key.ProductName, g.Key.UnitName, g.Sum(x => x.Pending)))
            .OrderByDescending(x => x.Pending)
            .ThenBy(x => x.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PosKitchenBoardDto(
            till.Location.Id,
            till.Location.Name,
            [new PosKitchenStationOptionDto(null, KitchenStation.DefaultName), .. stations.Where(x => x.IsActive).Select(x => new PosKitchenStationOptionDto(x.Id, x.Name))],
            pending.Count,
            tickets,
            summary,
            orders.Count == 0 ? null : orders.Max(x => x.LastActivityAt),
            now);
    }
}
