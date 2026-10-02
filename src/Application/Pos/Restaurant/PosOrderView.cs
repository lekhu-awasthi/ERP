using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>
/// One order line with every quantity a screen shows. All but <paramref name="Served"/> are sums over
/// the line's kitchen ticket lines (see <see cref="PosOrder"/>); <paramref name="Quantity"/> is the net
/// a guest pays for, and the money is that quantity priced by the till's one arithmetic.
/// </summary>
public sealed record PosOrderLineDto(
    Guid Id,
    int LineNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid? UnitId,
    string? UnitName,
    decimal Rate,
    VatRate VatRate,
    decimal ServiceChargeRate,
    string? Note,
    Guid? KitchenStationId,
    string KitchenStationName,
    decimal Ordered,
    decimal Discarded,
    decimal Quantity,
    decimal Served,
    decimal Outstanding,
    decimal Invoiced,
    decimal ToBill,
    decimal Amount,
    decimal ServiceChargeAmount,
    decimal VatAmount,
    decimal Total);

public sealed record KitchenTicketLineDto(
    Guid OrderLineId, int LineNo, string ProductName, string? UnitName, decimal Quantity, string? Note);

/// <param name="Number">What the paper prints: the order's code and the send, <c>ORD0007-2</c>.</param>
public sealed record KitchenTicketDto(
    Guid Id,
    int SendNumber,
    string Number,
    Guid? KitchenStationId,
    string KitchenStationName,
    bool IsCancellation,
    string? Reason,
    DateTimeOffset CreatedAt,
    string CreatedByName,
    int PrintCount,
    IReadOnlyList<KitchenTicketLineDto> Lines);

/// <summary>
/// An order as the restaurant till and the ERP list show it. <paramref name="Total"/> is the estimate
/// before any bill: the lines' amount, service charge and VAT, not yet rounded to the rupee (the bill
/// rounds, phase 65). Phase 65: <paramref name="Billed"/> is what its invoices not voided came to, and
/// <paramref name="Invoices"/> lists every bill, voided ones included.
/// </summary>
public sealed record PosOrderDto(
    Guid Id,
    string Code,
    Guid LocationId,
    string LocationCode,
    string LocationName,
    PosTab OrderType,
    PosOrderStatus Status,
    Guid? TableId,
    string? TableName,
    Guid? AreaId,
    string? AreaName,
    int Covers,
    Guid? ContactId,
    string ContactName,
    DateOnly Date,
    DateTimeOffset CreatedAt,
    string CreatedByName,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    string? VoidedByName,
    decimal Amount,
    decimal ServiceCharge,
    decimal Vat,
    decimal Total,
    decimal Outstanding,
    DateTimeOffset? SettledAt,
    decimal Billed,
    decimal ToBill,
    IReadOnlyList<PosOrderLineDto> Lines,
    IReadOnlyList<KitchenTicketDto> Tickets,
    IReadOnlyList<PosOrderInvoiceDto> Invoices);

/// <summary>
/// Phase 64 -- builds <see cref="PosOrderDto"/>s: one order for the till, or a page of them for the ERP
/// list, with every name each needs read in one query per kind rather than one per order (phase 34c).
/// </summary>
internal static class PosOrderView
{
    public static async Task<PosOrderDto> ReadAsync(IAppDbContext db, PosOrder order, CancellationToken cancellationToken) =>
        (await ReadManyAsync(db, order.OrganizationId, [order], cancellationToken))[0];

    public static async Task<IReadOnlyList<PosOrderDto>> ReadManyAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyList<PosOrder> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
        {
            return [];
        }

        var productIds = orders.SelectMany(o => o.Lines.Select(l => l.ProductId)).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == organizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var unitIds = orders.SelectMany(o => o.Lines.Where(l => l.UnitId != null).Select(l => l.UnitId!.Value))
            .Distinct().ToList();
        var units = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == organizationId && unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);

        var stations = await db.KitchenStations
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var locationIds = orders.Select(o => o.BillingLocationId).Distinct().ToList();
        var locations = await db.BillingLocations
            .Where(x => x.OrganizationId == organizationId && locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var tableIds = orders.Where(o => o.PosTableId != null).Select(o => o.PosTableId!.Value).Distinct().ToList();
        var tables = await (
                from table in db.PosTables
                join area in db.PosAreas on table.PosAreaId equals area.Id
                where table.OrganizationId == organizationId && tableIds.Contains(table.Id)
                select new { table.Id, table.Name, AreaId = area.Id, AreaName = area.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var contactIds = orders.Where(o => o.ContactId != null).Select(o => o.ContactId!.Value).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == organizationId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var walkInName = orders.Any(o => o.ContactId is null)
            ? await db.Contacts
                .Where(x => x.OrganizationId == organizationId && x.IsWalkInCustomer)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var userIds = orders
            .SelectMany(o => o.Tickets.Select(t => t.CreatedByUserId)
                .Append(o.CreatedByUserId)
                .Concat(o.VoidedByUserId is { } v ? new[] { v } : Array.Empty<Guid>()))
            .Distinct()
            .ToList();
        var users = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var billing = await PosOrderBilling.LoadManyAsync(
            db, organizationId, [.. orders.Select(o => o.Id)], cancellationToken);

        string StationName(Guid? id) => id is { } s ? stations.GetValueOrDefault(s, "") : KitchenStation.DefaultName;
        string? UnitName(Guid? id) => id is { } u ? units.GetValueOrDefault(u) : null;

        return orders.Select(order =>
        {
            var billed = billing.GetValueOrDefault(order.Id) ?? PosOrderBilledState.Nothing;
            var invoiced = billed.Invoiced;

            var lines = order.Lines
                .OrderBy(x => x.LineNo)
                .Select(line =>
                {
                    var q = order.QuantitiesOf(line);
                    var figures = line.Figures(q.Net);
                    var product = products.GetValueOrDefault(line.ProductId);

                    return new PosOrderLineDto(
                        line.Id, line.LineNo, line.ProductId, product?.Code ?? "", product?.Name ?? "",
                        line.UnitId, UnitName(line.UnitId), line.Rate, line.VatRate, line.ServiceChargeRate,
                        line.Note, line.KitchenStationId, StationName(line.KitchenStationId),
                        q.Ordered, q.Discarded, q.Net, q.Served, q.Outstanding,
                        invoiced.GetValueOrDefault(line.Id), order.RemainingToBill(line, invoiced),
                        figures.Amount, figures.ServiceChargeAmount, figures.VatAmount,
                        figures.Amount + figures.ServiceChargeAmount + figures.VatAmount);
                })
                .ToList();

            var lineNos = order.Lines.ToDictionary(x => x.Id);

            var tickets = order.Tickets
                .OrderBy(x => x.SendNumber)
                .ThenBy(x => x.KitchenStationId is null ? 1 : 0)
                .ThenBy(x => StationName(x.KitchenStationId), StringComparer.OrdinalIgnoreCase)
                .Select(ticket => new KitchenTicketDto(
                    ticket.Id,
                    ticket.SendNumber,
                    $"{order.Code}-{ticket.SendNumber}",
                    ticket.KitchenStationId,
                    StationName(ticket.KitchenStationId),
                    ticket.IsCancellation,
                    ticket.Reason,
                    ticket.CreatedAt,
                    users.GetValueOrDefault(ticket.CreatedByUserId, ""),
                    ticket.PrintCount,
                    ticket.Lines
                        .Select(row =>
                        {
                            var line = lineNos[row.PosOrderLineId];
                            return new KitchenTicketLineDto(
                                line.Id, line.LineNo, products.GetValueOrDefault(line.ProductId)?.Name ?? "",
                                UnitName(line.UnitId), row.Quantity, line.Note);
                        })
                        .OrderBy(x => x.LineNo)
                        .ToList()))
                .ToList();

            var location = locations.GetValueOrDefault(order.BillingLocationId);
            var table = order.PosTableId is { } t ? tables.GetValueOrDefault(t) : null;
            var estimate = order.Estimate();

            return new PosOrderDto(
                order.Id,
                order.Code,
                order.BillingLocationId,
                location?.Code ?? "",
                location?.Name ?? "",
                order.OrderType,
                order.Status,
                order.PosTableId,
                table?.Name,
                table?.AreaId,
                table?.AreaName,
                order.Covers,
                order.ContactId,
                order.ContactId is { } c ? contacts.GetValueOrDefault(c, "") : walkInName ?? "Walk-in customer",
                order.Date,
                order.CreatedAt,
                users.GetValueOrDefault(order.CreatedByUserId, ""),
                order.VoidReason,
                order.VoidedAt,
                order.VoidedByUserId is { } voidedBy ? users.GetValueOrDefault(voidedBy) : null,
                estimate.Amount,
                estimate.ServiceChargeAmount,
                estimate.VatAmount,
                estimate.Amount + estimate.ServiceChargeAmount + estimate.VatAmount,
                lines.Sum(x => x.Outstanding),
                order.SettledAt,
                billed.Totals.GrandTotal,
                lines.Sum(x => x.ToBill),
                lines,
                tickets,
                billed.Invoices);
        }).ToList();
    }
}
