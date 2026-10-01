using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Sessions;

/// <summary>What the tills took in one payment mode.</summary>
public sealed record PosTenderTotalDto(Guid PaymentModeId, string PaymentModeName, PaymentModeKind Kind, decimal Amount);

/// <summary>
/// The sales figures of a set of till sales -- one session's, or one day's.
/// </summary>
/// <param name="SubTotal">The lines' amounts after discount, before service charge and VAT.</param>
/// <param name="GrandTotal">What customers owed: sub-total, service charge, VAT and round-off.</param>
/// <param name="Tendered">Everything handed over, in every mode, before change.</param>
/// <param name="Settled">What the tenders settled: tendered less change.</param>
/// <param name="Credit">What was left on customers' accounts: grand total less settled.</param>
/// <param name="CashSales">What the sales put into the drawer: cash tendered less change given.</param>
public sealed record PosSalesSummaryDto(
    int SalesCount,
    decimal SubTotal,
    decimal ServiceCharge,
    decimal Vat,
    decimal RoundOff,
    decimal GrandTotal,
    IReadOnlyList<PosTenderTotalDto> Tenders,
    decimal Tendered,
    decimal Change,
    decimal Settled,
    decimal Credit,
    decimal CashSales);

/// <summary>
/// Phase 61 -- <b>the one place a till's takings are added up</b> (phase 59 Decision H, against the
/// vendor's defect 7). Its Day Report said <i>Total Sales 610.20</i> and its session said <i>Sales
/// Transactions 611.00</i> about the same day, because the two were computed apart: one unrounded
/// and missing a service charge, the other from the invoices. Phase 36's rule is that two figures
/// agree only through one shared reader plus a test that reads both, and this is that reader --
/// <c>ForSessionAsync</c> and <c>ForDayAsync</c> differ only in which invoices they select and share
/// every line of the arithmetic.
///
/// <para><b>What counts.</b> Approved till sales (<see cref="SalesChannel.Pos"/>). A void takes a sale
/// out: it is as if it never happened, and its reversal takes the cash back out of the drawer's
/// account. That is safe for the session's expected cash only because a void is refused once the
/// session has closed (<c>VoidInvoiceCommandHandler</c>), so a closed session's figures never move.</para>
///
/// <para><b>Shape.</b> Line and tender totals are summed per invoice store-side over the same invoice
/// <i>query</i> (phase 42: join the query, never hand SQL a materialised id list), then added up here.
/// Every money figure is a till figure, so it is already the base currency (Decision E).</para>
/// </summary>
internal static class PosSalesReader
{
    public static Task<PosSalesSummaryDto> ForSessionAsync(
        IAppDbContext db, Guid organizationId, Guid sessionId, CancellationToken cancellationToken) =>
        SummarizeAsync(db, TillSales(db, organizationId).Where(x => x.PosSessionId == sessionId), cancellationToken);

    /// <summary>One business day's till sales, at one location or all of them. The day is the
    /// invoice's own date, which a till stamps with the Nepal date it was rung up on.</summary>
    public static Task<PosSalesSummaryDto> ForDayAsync(
        IAppDbContext db, Guid organizationId, DateOnly date, Guid? locationId, CancellationToken cancellationToken)
    {
        var sales = TillSales(db, organizationId).Where(x => x.Date == date);

        if (locationId is { } onlyLocation)
        {
            sales = sales.Where(x => x.LocationId == onlyLocation);
        }

        return SummarizeAsync(db, sales, cancellationToken);
    }

    /// <summary>
    /// What a session's drawer should hold now: the float it opened with, every cash movement, and
    /// what its sales put in. The one formula both the live session view and the close use.
    /// </summary>
    public static decimal ExpectedCash(PosSession session, PosSalesSummaryDto sales) =>
        session.OpeningFloat + session.CashMovements.Sum(x => x.SignedAmount) + sales.CashSales;

    private static IQueryable<Invoice> TillSales(IAppDbContext db, Guid organizationId) =>
        db.Invoices.Where(x => x.OrganizationId == organizationId
            && x.Channel == SalesChannel.Pos && x.Status == InvoiceStatus.Approved);

    private static async Task<PosSalesSummaryDto> SummarizeAsync(
        IAppDbContext db, IQueryable<Invoice> sales, CancellationToken cancellationToken)
    {
        var headers = await sales
            .Select(x => new { x.Id, x.RoundOff, x.ChangeAmount })
            .ToListAsync(cancellationToken);

        if (headers.Count == 0)
        {
            return new PosSalesSummaryDto(0, 0m, 0m, 0m, 0m, 0m, [], 0m, 0m, 0m, 0m, 0m);
        }

        var lineTotals = await (
                from line in db.InvoiceLines
                join invoice in sales on line.InvoiceId equals invoice.Id
                group line by line.InvoiceId into g
                select new
                {
                    Amount = g.Sum(x => x.Amount),
                    ServiceCharge = g.Sum(x => x.ServiceChargeAmount),
                    Vat = g.Sum(x => x.VatAmount),
                })
            .ToListAsync(cancellationToken);

        var tenderTotals = await (
                from tender in db.InvoiceTenders
                join invoice in sales on tender.InvoiceId equals invoice.Id
                group tender by new { tender.PaymentModeId, tender.Kind } into g
                select new { g.Key.PaymentModeId, g.Key.Kind, Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var modeIds = tenderTotals.Select(x => x.PaymentModeId).Distinct().ToList();
        var modeNames = await db.PaymentModes
            .Where(x => modeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var subTotal = lineTotals.Sum(x => x.Amount);
        var serviceCharge = lineTotals.Sum(x => x.ServiceCharge);
        var vat = lineTotals.Sum(x => x.Vat);
        var roundOff = headers.Sum(x => x.RoundOff);
        var grandTotal = subTotal + serviceCharge + vat + roundOff;
        var tendered = tenderTotals.Sum(x => x.Amount);
        var change = headers.Sum(x => x.ChangeAmount);
        var settled = tendered - change;
        var cashTendered = tenderTotals.Where(x => x.Kind == PaymentModeKind.Cash).Sum(x => x.Amount);

        return new PosSalesSummaryDto(
            headers.Count,
            subTotal,
            serviceCharge,
            vat,
            roundOff,
            grandTotal,
            [.. tenderTotals
                .Select(x => new PosTenderTotalDto(x.PaymentModeId, modeNames.GetValueOrDefault(x.PaymentModeId, ""), x.Kind, x.Amount))
                .OrderBy(x => x.Kind)
                .ThenBy(x => x.PaymentModeName, StringComparer.Ordinal)],
            tendered,
            change,
            settled,
            grandTotal - settled,
            cashTendered - change);
    }
}
