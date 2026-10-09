using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.Pos;

/// <summary>How the part being paid now is chosen (phase-65-status.md Decision D).</summary>
public enum PosOrderSplit
{
    /// <summary>Everything still to be billed, on one bill.</summary>
    Whole = 1,

    /// <summary>Chosen lines, each in a chosen quantity: the vendor's Split Bill (by item and by quantity
    /// are one request shape -- a whole line is just its whole remaining quantity).</summary>
    Items = 2,

    /// <summary>One of N equal parts of what is left: every line's remaining quantity divided by N.</summary>
    Equal = 3,
}

/// <summary>What is already billed of one order line, over its invoices not voided.</summary>
public sealed record PosOrderLineBilled(decimal Quantity, PosLineArithmetic.Figures Figures);

/// <summary>The order's invoices not voided, as one sum: before rounding, and as billed.</summary>
public sealed record PosOrderBilledTotals(decimal Unrounded, decimal GrandTotal)
{
    public static readonly PosOrderBilledTotals None = new(0m, 0m);
}

/// <param name="RemainingAfter">What of the line is still to be billed once this part is paid.</param>
public sealed record PosOrderBillLine(PosOrderLine Line, decimal Quantity, PosLineArithmetic.Figures Figures, decimal RemainingAfter)
{
    public decimal Total => Figures.Total;
}

/// <summary>A part of an order priced, before it is an invoice.</summary>
/// <param name="Unrounded">The lines' amount, service charge, packaging fee and VAT.</param>
/// <param name="RoundOff">Added to <paramref name="Unrounded"/> to make <paramref name="Total"/>.</param>
/// <param name="BillsTheRest">Nothing is left to bill once this part is paid.</param>
/// <param name="OrderTotal">What the whole order comes to as bills: the running total rounded, once the
/// last part is paid. Every part's <paramref name="Total"/> adds up to it.</param>
public sealed record PosOrderBillPlan(
    IReadOnlyList<PosOrderBillLine> Lines,
    decimal Unrounded,
    decimal RoundOff,
    decimal Total,
    bool BillsTheRest,
    decimal OrderTotal,
    decimal BilledBefore)
{
    public decimal ServiceCharge => Lines.Sum(x => x.Figures.ServiceChargeAmount);

    public decimal PackagingFee => Lines.Sum(x => x.Figures.PackagingFeeAmount);

    /// <summary>What is left for later parts after this one: the order's total less everything billed.</summary>
    public decimal LeftAfter => OrderTotal - BilledBefore - Total;
}

/// <summary>
/// Phase 65 -- prices the part of an order being paid now. Pure: the caller reads what is already billed
/// (a sum over the invoice lines naming each order line, on invoices not voided) and this decides the
/// rest, so the till's preview and the bill that posts are one computation (phase 63's rule for a figure
/// that depends on other documents; phase-65-status.md Decision E).
///
/// <para><b>Rates never move; quantities do.</b> Every line is priced from the order line's frozen rate,
/// VAT rate, service charge rate and packaging fee (phase 64 Decision C; phase 70), so a split cannot drop a service charge the way
/// the vendor's did (phase 59 defect 1: the remainder billed 20 of service charge instead of 40). Phase 72 -- and
/// from the discounts and the waiver (<see cref="PosOrder.FiguresOf"/>), which the order fixes at its first bill
/// for the same reason.</para>
///
/// <para><b>The last of a line takes exactly what is left of its money</b> -- its amount, service charge,
/// packaging fee and VAT, each -- so however a line is split, its parts add up to what the whole line comes to, to the
/// paisa. Any other part is priced fresh by <see cref="PosLineArithmetic"/>.</para>
///
/// <para><b>Rounding is cumulative.</b> A part comes to the running total rounded, less what is already
/// billed: <c>R(billed so far + this part) − billed so far</c>. So every bill is a whole rupee, every
/// round-off is under a rupee either way, and the parts add up to the order's own rounded total exactly
/// -- 633 paid in halves is 316 then 317, the same answer phase 63's last refund gives, reached without
/// a special case for the last part.</para>
/// </summary>
public static class PosOrderBill
{
    public static PosOrderBillPlan Plan(
        PosOrder order,
        IReadOnlyDictionary<Guid, PosOrderLineBilled> billed,
        PosOrderBilledTotals billedTotals,
        PosOrderSplit split,
        IReadOnlyList<PosOrderLineQuantity> items,
        int parts,
        bool roundOff)
    {
        if (order.Status != PosOrderStatus.Open)
        {
            throw new InvalidOperationException(order.Status == PosOrderStatus.Settled
                ? $"Order {order.Code} is fully billed."
                : $"Order {order.Code} is voided, so there is nothing to bill.");
        }

        var invoiced = billed.ToDictionary(x => x.Key, x => x.Value.Quantity);
        var remaining = order.Lines.ToDictionary(x => x.Id, x => order.RemainingToBill(x, invoiced));

        if (remaining.Values.All(x => x <= 0m))
        {
            throw new InvalidOperationException($"Order {order.Code} has nothing left to bill.");
        }

        var chosen = Choose(order, remaining, split, items, parts);

        if (chosen.Count == 0)
        {
            throw new InvalidOperationException("Choose something to bill.");
        }

        var lines = chosen
            .Select(x =>
            {
                var left = remaining[x.Line.Id];
                var figures = x.Quantity == left
                    ? WhatIsLeft(order, x.Line, billed.GetValueOrDefault(x.Line.Id))
                    : order.FiguresOf(x.Line, x.Quantity);
                return new PosOrderBillLine(x.Line, x.Quantity, figures, left - x.Quantity);
            })
            .ToList();

        var unrounded = lines.Sum(x => x.Total);
        var billsTheRest = order.Lines.All(x =>
            remaining[x.Id] - lines.Where(l => l.Line.Id == x.Id).Sum(l => l.Quantity) == 0m);

        var total = RunningTotal(billedTotals, unrounded, roundOff);

        // The order's total once every part is paid: what is billed, this part, and every remaining
        // quantity priced as the rest of its line -- the running total rounded at the end.
        var restUnrounded = order.Lines.Sum(line =>
        {
            var after = remaining[line.Id] - lines.Where(l => l.Line.Id == line.Id).Sum(l => l.Quantity);
            if (after <= 0m)
            {
                return 0m;
            }

            var already = lines.Where(l => l.Line.Id == line.Id)
                .Aggregate(billed.GetValueOrDefault(line.Id)?.Figures ?? PosLineArithmetic.Figures.Zero, (sum, l) => sum + l.Figures);
            var whole = order.FiguresOf(line, order.QuantitiesOf(line).Net);
            return (whole - already).Total;
        });

        var orderTotal = Round(billedTotals.Unrounded + unrounded + restUnrounded, roundOff);

        return new PosOrderBillPlan(
            lines, unrounded, total - unrounded, total, billsTheRest, orderTotal, billedTotals.GrandTotal);
    }

    /// <summary>
    /// <c>R(billed before rounding + this part) − billed</c>. A void of an earlier part can leave the
    /// surviving bills a rupee or so off the running total; when the formula would then make this bill
    /// negative or move it by a rupee or more, the part is rounded on its own instead, and the identity
    /// "the parts add up to the order" is short by what the voided part rounded.
    /// </summary>
    private static decimal RunningTotal(PosOrderBilledTotals billed, decimal unrounded, bool roundOff)
    {
        var candidate = Round(billed.Unrounded + unrounded, roundOff) - billed.GrandTotal;

        return candidate < 0m || Math.Abs(candidate - unrounded) >= 1m
            ? Round(unrounded, roundOff)
            : candidate;
    }

    private static decimal Round(decimal value, bool roundOff) =>
        roundOff ? decimal.Round(value, 0, MidpointRounding.AwayFromZero) : value;

    private static List<(PosOrderLine Line, decimal Quantity)> Choose(
        PosOrder order,
        Dictionary<Guid, decimal> remaining,
        PosOrderSplit split,
        IReadOnlyList<PosOrderLineQuantity> items,
        int parts)
    {
        var open = order.Lines.OrderBy(x => x.LineNo).Where(x => remaining[x.Id] > 0m).ToList();

        switch (split)
        {
            case PosOrderSplit.Whole:
                return [.. open.Select(x => (x, remaining[x.Id]))];

            case PosOrderSplit.Equal:
                if (parts < 1 || parts > PosOrder.MaxCovers)
                {
                    throw new InvalidOperationException($"An equal split is into 1 to {PosOrder.MaxCovers} parts.");
                }

                return [.. open
                    .Select(x => (Line: x, Quantity: parts == 1
                        ? remaining[x.Id]
                        : Math.Min(remaining[x.Id], decimal.Round(remaining[x.Id] / parts, UnitConversion.QuantityScale, MidpointRounding.AwayFromZero))))
                    .Where(x => x.Quantity > 0m)];

            case PosOrderSplit.Items:
                if (items.Select(x => x.LineId).Distinct().Count() != items.Count)
                {
                    throw new InvalidOperationException("A line is named twice.");
                }

                var byId = order.Lines.ToDictionary(x => x.Id);
                var chosen = new List<(PosOrderLine Line, decimal Quantity)>();

                foreach (var item in items)
                {
                    if (!byId.TryGetValue(item.LineId, out var line))
                    {
                        throw new InvalidOperationException("That line is not on this order.");
                    }

                    if (item.Quantity <= 0m || decimal.Round(item.Quantity, UnitConversion.QuantityScale) != item.Quantity)
                    {
                        throw new InvalidOperationException(
                            $"A quantity is more than zero, with at most {UnitConversion.QuantityScale} decimal places.");
                    }

                    if (item.Quantity > remaining[line.Id])
                    {
                        throw new InvalidOperationException(
                            $"Line {line.LineNo} has {remaining[line.Id]:0.####} left to bill, not {item.Quantity:0.####}.");
                    }

                    chosen.Add((line, item.Quantity));
                }

                return [.. chosen.OrderBy(x => x.Line.LineNo)];

            default:
                throw new InvalidOperationException($"'{split}' is not a way to split a bill.");
        }
    }

    /// <summary>The whole line at its net quantity, less what earlier bills took of it: each figure on
    /// its own, so the line's service charge, packaging fee and VAT add up across its parts as exactly as
    /// its amount.</summary>
    private static PosLineArithmetic.Figures WhatIsLeft(PosOrder order, PosOrderLine line, PosOrderLineBilled? billed)
    {
        var whole = order.FiguresOf(line, order.QuantitiesOf(line).Net);
        return billed is null ? whole : whole - billed.Figures;
    }
}
