using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Trade.Queries.SalesSummaryReport;

/// <summary>
/// Sales totals for one BS fiscal year, one row per BS month or per day with activity.
///
/// <para><b>The taxable/non-taxable split follows the line's VAT rate, not its VAT amount.</b> A
/// zero-rated line and an exempt line both carry a VAT amount of zero, so counting zero-VAT money
/// as non-taxable would fold zero-rated (export) sales into the exempt bucket. Only
/// <see cref="VatRate.ThirteenPercentVat"/> is taxable here -- the same reading
/// <c>VatRateExtensions.ToPercent</c> already encodes.</para>
///
/// <para><b>Sub Total is gross of discount</b>, matching the live column order (Sub Total, then
/// Discount, then the two sales buckets): Sub Total less Discount equals Non Taxable plus Taxable,
/// and Total is those plus VAT.</para>
/// </summary>
public sealed class SalesSummaryReportQueryHandler(IAppDbContext db)
    : IRequestHandler<SalesSummaryReportQuery, SalesSummaryReportDto>
{
    public async Task<SalesSummaryReportDto> Handle(SalesSummaryReportQuery request, CancellationToken cancellationToken)
    {
        var months = TradeMonthlyCrosstab.Columns(request.FiscalYear)
            ?? throw new NotFoundException(
                $"Fiscal year {request.FiscalYear} is outside the supported Bikram Sambat range.");

        var fromDate = months[0].FromDate;
        var toDate = months[^1].ToDate;

        var facts = await TradeLineReader.LoadAsync(
            db, request.OrganizationId, TradeSide.Sales, fromDate, toDate, cancellationToken);

        var rows = request.Mode == SalesSummaryMode.Month
            ? BuildMonthRows(facts, months)
            : BuildDateRows(facts);

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new SalesSummaryReportDto(
            request.FiscalYear, request.Mode, fromDate, toDate,
            paged.Items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    /// <summary>Fiscal order, Shrawan first -- and only months that have activity, which is what the
    /// live report returns (two rows on a three-year tenant, not twelve).</summary>
    private static List<SalesSummaryRowDto> BuildMonthRows(
        List<TradeLineReader.Fact> facts, IReadOnlyList<BsFiscalMonth> months)
    {
        var rows = new List<SalesSummaryRowDto>();

        foreach (var month in months)
        {
            var inMonth = facts.Where(x => x.Date >= month.FromDate && x.Date <= month.ToDate).ToList();
            if (inMonth.Count == 0)
            {
                continue;
            }

            rows.Add(Summarise(null, $"{month.MonthName}, {month.BsYear}", inMonth));
        }

        return [.. rows.Where(IsNonZero)];
    }

    /// <summary>Newest first, as the live Date mode renders it.</summary>
    private static List<SalesSummaryRowDto> BuildDateRows(List<TradeLineReader.Fact> facts) =>
    [
        .. facts
            .GroupBy(x => x.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => Summarise(g.Key, null, [.. g]))
            .Where(IsNonZero),
    ];

    private static bool IsNonZero(SalesSummaryRowDto row) =>
        row.SubTotal != 0 || row.Discount != 0 || row.NonTaxableSales != 0
        || row.TaxableSales != 0 || row.Vat != 0 || row.Total != 0;

    private static SalesSummaryRowDto Summarise(
        DateOnly? date, string? label, IReadOnlyCollection<TradeLineReader.Fact> facts)
    {
        var subTotal = facts.Sum(x => x.Amount);
        var discount = facts.Sum(x => x.Discount);
        var taxable = facts.Where(x => x.VatRate == VatRate.ThirteenPercentVat).Sum(x => x.NetAmount);
        var nonTaxable = facts.Where(x => x.VatRate != VatRate.ThirteenPercentVat).Sum(x => x.NetAmount);
        var vat = facts.Sum(x => x.VatAmount);

        return new SalesSummaryRowDto(date, label, subTotal, discount, nonTaxable, taxable, vat, nonTaxable + taxable + vat);
    }
}
