using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>The part of an order being billed, as a request names it.</summary>
/// <param name="Items">For <see cref="PosOrderSplit.Items"/>: each order line and how much of it.</param>
/// <param name="Parts">For <see cref="PosOrderSplit.Equal"/>: how many equal parts what is left is split
/// into, this one included. The till counts it down as the parts are paid; the server keeps no split state.</param>
/// <param name="Discount">Phase 83 -- this part's own discount, if any: one guest's, on this bill only.</param>
/// <param name="WaiveServiceCharge">Phase 91 -- this part's guest declines the service charge, on this bill only.</param>
public sealed record PosOrderBillPart(
    PosOrderSplit Split,
    IReadOnlyList<PosOrderLineQuantityInput>? Items,
    int? Parts,
    PosPartDiscount? Discount,
    bool WaiveServiceCharge = false)
{
    /// <summary>Phase 83 -- a request's <c>discountPct</c> or <c>discountAmount</c> (the validators allow one at
    /// most) as the part's discount; neither, or zero, is none.</summary>
    public static PosPartDiscount? DiscountOf(decimal? discountPct, decimal? discountAmount) =>
        discountAmount is { } amount and not 0m ? new PosPartDiscount(PosDiscountKind.Amount, amount)
        : discountPct is { } pct and not 0m ? new PosPartDiscount(PosDiscountKind.Percent, pct)
        : null;
}

/// <summary>One line of a planned bill, named for the screen and the estimate. Phase 72:
/// <paramref name="ServiceChargeRate"/> is the rate the line is billed at (none when the order waived it),
/// <paramref name="DiscountPct"/> the line's own discount, and <paramref name="Gross"/> its quantity at its
/// rate before any discount.</summary>
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
    decimal PackagingFeeAmount,
    decimal VatAmount,
    decimal Total,
    decimal RemainingAfter,
    decimal DiscountPct = 0m,
    decimal Gross = 0m,
    decimal DiscountPerUnit = 0m,
    decimal BillDiscountShare = 0m,
    decimal PartDiscountShare = 0m,
    decimal ServiceChargeWaived = 0m);

/// <summary>
/// A part of an order priced by the server, which is what the till shows before money changes hands and
/// what the estimate bill prints. <paramref name="Total"/> is what this bill comes to;
/// <paramref name="OrderTotal"/> what the whole order comes to as bills; <paramref name="LeftAfter"/> what
/// later parts will pay. Phase 72: <paramref name="Gross"/> less <paramref name="Discount"/> is
/// <paramref name="Amount"/>, as on the receipt; <paramref name="DiscountPct"/> and
/// <paramref name="ServiceChargeWaived"/> are the order's terms the part was priced with. Phase 83:
/// <paramref name="PartDiscount"/> is what this bill's own discount took off, inside <paramref name="Discount"/>;
/// <paramref name="DiscountAmount"/> stays the part's share of the order's Amount. Phase 91: <paramref name="PartServiceChargeWaived"/>
/// is the service charge this bill's guest declined, which <paramref name="ServiceCharge"/> does not include.
/// </summary>
public sealed record PosOrderBillPreviewDto(
    Guid OrderId,
    string OrderCode,
    IReadOnlyList<PosOrderBillLineDto> Lines,
    decimal Amount,
    decimal ServiceCharge,
    decimal PackagingFee,
    decimal Vat,
    decimal Unrounded,
    decimal RoundOff,
    decimal Total,
    bool BillsTheRest,
    decimal OrderTotal,
    decimal BilledBefore,
    decimal LeftAfter,
    decimal Gross = 0m,
    decimal Discount = 0m,
    decimal DiscountPct = 0m,
    bool ServiceChargeWaived = false,
    decimal DiscountAmount = 0m,
    decimal PartDiscount = 0m,
    decimal PartServiceChargeWaived = 0m);

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
            till.Settings.RoundOffEnabled,
            part.Discount,
            part.WaiveServiceCharge));

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
                // Phase 91 -- a part whose guest declined the service charge is billed at none.
                return new PosOrderBillLineDto(
                    x.Line.Id, x.Line.LineNo, name.ProductName, name.UnitName, x.Line.Note, x.Quantity, x.Line.Rate,
                    x.ServiceChargeWaived > 0m ? 0m : order.ServiceChargeRateOf(x.Line), x.Figures.Amount,
                    x.Figures.ServiceChargeAmount, x.Figures.PackagingFeeAmount, x.Figures.VatAmount, x.Total, x.RemainingAfter,
                    x.Line.DiscountPct, PosLineArithmetic.RoundMoney(x.Quantity * x.Line.Rate), x.Line.DiscountPerUnit,
                    x.BillDiscountShare, x.PartDiscountShare, x.ServiceChargeWaived);
            })
            .ToList();

        // Gross is per line at the paisa, as the receipt's is, so Gross − Discount = Amount on the estimate too.
        var gross = lines.Sum(x => x.Gross);
        var amount = lines.Sum(x => x.Amount);

        return new PosOrderBillPreviewDto(
            order.Id,
            order.Code,
            lines,
            amount,
            lines.Sum(x => x.ServiceChargeAmount),
            lines.Sum(x => x.PackagingFeeAmount),
            lines.Sum(x => x.VatAmount),
            plan.Unrounded,
            plan.RoundOff,
            plan.Total,
            plan.BillsTheRest,
            plan.OrderTotal,
            plan.BilledBefore,
            plan.LeftAfter,
            gross,
            gross - amount,
            order.DiscountPct,
            order.ServiceChargeWaived,
            lines.Sum(x => x.BillDiscountShare),
            plan.PartDiscount,
            plan.ServiceChargeWaived);
    }
}
