using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Restaurant;

/// <summary>One bill an order has had, for the till and the ERP list.</summary>
public sealed record PosOrderInvoiceDto(
    Guid Id, string Code, decimal GrandTotal, decimal ServiceCharge, decimal RoundOff, bool IsVoided);

/// <summary>What an order's invoices billed: per order line, as one total, and as the list.</summary>
internal sealed record PosOrderBilledState(
    IReadOnlyDictionary<Guid, PosOrderLineBilled> Lines,
    PosOrderBilledTotals Totals,
    IReadOnlyList<PosOrderInvoiceDto> Invoices)
{
    public IReadOnlyDictionary<Guid, decimal> Invoiced =>
        Lines.ToDictionary(x => x.Key, x => x.Value.Quantity);

    public static readonly PosOrderBilledState Nothing =
        new(new Dictionary<Guid, PosOrderLineBilled>(), PosOrderBilledTotals.None, []);
}

/// <summary>
/// Phase 65 -- the one reader of what an order has been billed: a sum over the invoice lines naming each
/// order line, on the order's invoices that are not voided (phase 64 Decision B; phase 6's caps net of
/// reversals). The split planner, the discard and void refusals, the settled check and every screen read
/// it, so none of them can disagree about what is left to bill.
/// </summary>
internal static class PosOrderBilling
{
    public static async Task<PosOrderBilledState> LoadAsync(
        IAppDbContext db, Guid organizationId, Guid orderId, CancellationToken cancellationToken) =>
        (await LoadManyAsync(db, organizationId, [orderId], cancellationToken)).GetValueOrDefault(orderId)
        ?? PosOrderBilledState.Nothing;

    public static async Task<IReadOnlyDictionary<Guid, PosOrderBilledState>> LoadManyAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, PosOrderBilledState>();
        }

        // Voided bills are listed (the order's history), and counted nowhere.
        var invoices = await db.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganizationId == organizationId && x.PosOrderId != null && orderIds.Contains(x.PosOrderId.Value)
                && x.Status != InvoiceStatus.Draft)
            .OrderBy(x => x.ApprovedAt)
            .ToListAsync(cancellationToken);

        return invoices
            .GroupBy(x => x.PosOrderId!.Value)
            .ToDictionary(g => g.Key, g => Summarise(g.ToList()));
    }

    private static PosOrderBilledState Summarise(IReadOnlyList<Invoice> invoices)
    {
        var live = invoices.Where(x => x.Status == InvoiceStatus.Approved).ToList();

        var lines = live
            .SelectMany(x => x.Lines)
            .Where(x => x.PosOrderLineId != null)
            .GroupBy(x => x.PosOrderLineId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new PosOrderLineBilled(
                    g.Sum(x => x.Quantity),
                    new PosLineArithmetic.Figures(
                        g.Sum(x => x.Amount), g.Sum(x => x.ServiceChargeAmount), g.Sum(x => x.VatAmount))));

        var totals = new PosOrderBilledTotals(
            live.Sum(x => x.Lines.Sum(l => l.LineTotal)),
            live.Sum(x => x.GrandTotal));

        var list = invoices
            .Select(x => new PosOrderInvoiceDto(
                x.Id, x.Code, x.GrandTotal, x.ServiceChargeTotal, x.RoundOff, x.Status == InvoiceStatus.Void))
            .ToList();

        return new PosOrderBilledState(lines, totals, list);
    }
}
