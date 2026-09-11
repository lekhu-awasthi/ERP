using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Trade.Queries.TradeByItemMonthly;

/// <summary>The BS fiscal-year crosstab, grouped by product. See
/// <c>TradeByContactMonthlyQueryHandler</c>, which this mirrors exactly.</summary>
public sealed class TradeByItemMonthlyQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<TradeByItemMonthlyQuery, TradeByItemMonthlyDto>
{
    public async Task<TradeByItemMonthlyDto> Handle(TradeByItemMonthlyQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change. Narrows rows in addition to request.LocationId, which is the
        // user's own filter -- two mechanisms, two reasons, both applied.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var months = TradeMonthlyCrosstab.Columns(request.FiscalYear)
            ?? throw new NotFoundException(
                $"Fiscal year {request.FiscalYear} is outside the supported Bikram Sambat range.");

        var fromDate = months[0].FromDate;
        var toDate = months[^1].ToDate;

        var facts = await TradeLineReader.LoadAsync(
            db, request.OrganizationId, request.Side, fromDate, toDate, cancellationToken,
            request.LocationId, reportLocations);

        var productIds = facts.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = facts
            .Where(x => products.ContainsKey(x.ProductId))
            .GroupBy(x => x.ProductId)
            .Select(g =>
            {
                var product = products[g.Key];
                var monthly = TradeMonthlyCrosstab.Bucket(months, g.Select(x => (x.Date, x.NetAmount)));

                return new TradeByItemMonthlyRowDto(
                    product.Id,
                    product.Code,
                    product.Name,
                    monthly,
                    TradeMonthlyCrosstab.Quarters(monthly),
                    monthly.Sum());
            })
            .Where(x => x.Total != 0 || x.Monthly.Any(m => m != 0))
            .OrderBy(x => x.ProductName, StringComparer.Ordinal)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        var totalMonthly = new decimal[months.Count];
        foreach (var row in rows)
        {
            for (var i = 0; i < months.Count; i++)
            {
                totalMonthly[i] += row.Monthly[i];
            }
        }

        return new TradeByItemMonthlyDto(
            request.Side,
            request.FiscalYear,
            fromDate,
            toDate,
            [.. months.Select(TradeMonthlyColumnDto.From)],
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            totalMonthly,
            TradeMonthlyCrosstab.Quarters(totalMonthly),
            totalMonthly.Sum());
    }
}
