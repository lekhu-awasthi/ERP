using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Pos;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>The part of an order being billed, as a request names it.</summary>
/// <param name="Items">For <see cref="PosOrderSplit.Items"/>: each order line and how much of it.</param>
/// <param name="Parts">For <see cref="PosOrderSplit.Equal"/>: how many equal parts what is left is split
/// into, this one included. The till counts it down as the parts are paid; the server keeps no split state.</param>
public sealed record PosOrderBillPart(PosOrderSplit Split, IReadOnlyList<PosOrderLineQuantityInput>? Items, int? Parts);

/// <summary>One line of a planned bill, named for the screen and the estimate.</summary>
public sealed record PosOrderBillLineDto(
    Guid OrderLineId,
    int LineNo,
    string ProductName,
    string? UnitName,
    string? Note,
    decimal Quantity,
    decimal Rate,
    decimal ServiceChargeRate,
    decimal Amount,
    decimal ServiceChargeAmount,
    decimal VatAmount,
    decimal Total,
    decimal RemainingAfter);

/// <summary>
/// A part of an order priced by the server, which is what the till shows before money changes hands and
/// what the estimate bill prints. <paramref name="Total"/> is what this bill comes to;
/// <paramref name="OrderTotal"/> what the whole order comes to as bills; <paramref name="LeftAfter"/> what
/// later parts will pay.
/// </summary>
public sealed record PosOrderBillPreviewDto(
    Guid OrderId,
    string OrderCode,
    IReadOnlyList<PosOrderBillLineDto> Lines,
    decimal Amount,
    decimal ServiceCharge,
    decimal Vat,
    decimal Unrounded,
    decimal RoundOff,
    decimal Total,
    bool BillsTheRest,
    decimal OrderTotal,
    decimal BilledBefore,
    decimal LeftAfter);

/// <summary>
/// Phase 65 -- the one planner the preview, the estimate bill and the bill itself call (phase 63's rule:
/// a figure that depends on other documents is computed in one place, on the server, and the till keeps
/// no copy). It reads what the order's invoices already billed and hands the rest to the Domain's
/// <see cref="PosOrderBill"/>.
/// </summary>
internal static class PosOrderBillPlanner
{
    public static async Task<(PosOrderBillPlan Plan, PosOrderBilledState Billed)> PlanAsync(
        IAppDbContext db, Guid organizationId, PosOrder order, PosTillContext till, PosOrderBillPart part,
        CancellationToken cancellationToken)
    {
        var billed = await PosOrderBilling.LoadAsync(db, organizationId, order.Id, cancellationToken);

        var plan = PosOrderCommands.Run(() => PosOrderBill.Plan(
            order,
            billed.Lines,
            billed.Totals,
            part.Split,
            [.. (part.Items ?? []).Select(x => new PosOrderLineQuantity(x.LineId, x.Quantity))],
            part.Parts ?? 0,
            till.Settings.RoundOffEnabled));

        return (plan, billed);
    }

    public static async Task<PosOrderBillPreviewDto> ToPreviewAsync(
        IAppDbContext db, PosOrder order, PosOrderBillPlan plan, CancellationToken cancellationToken)
    {
        var view = await PosOrderView.ReadAsync(db, order, cancellationToken);
        var names = view.Lines.ToDictionary(x => x.Id);

        var lines = plan.Lines
            .Select(x =>
            {
                var name = names[x.Line.Id];
                return new PosOrderBillLineDto(
                    x.Line.Id, x.Line.LineNo, name.ProductName, name.UnitName, x.Line.Note, x.Quantity, x.Line.Rate,
                    x.Line.ServiceChargeRate, x.Figures.Amount, x.Figures.ServiceChargeAmount, x.Figures.VatAmount,
                    x.Total, x.RemainingAfter);
            })
            .ToList();

        return new PosOrderBillPreviewDto(
            order.Id,
            order.Code,
            lines,
            lines.Sum(x => x.Amount),
            lines.Sum(x => x.ServiceChargeAmount),
            lines.Sum(x => x.VatAmount),
            plan.Unrounded,
            plan.RoundOff,
            plan.Total,
            plan.BillsTheRest,
            plan.OrderTotal,
            plan.BilledBefore,
            plan.LeftAfter);
    }
}
