using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Trade.Queries.TradeByContactMonthly;

/// <summary>
/// The BS fiscal-year crosstab, grouped by contact. Facts are loaded over the fiscal year's own AD
/// range -- the calendar decides the window, the query does not take one -- then bucketed into the
/// twelve month columns by <see cref="TradeMonthlyCrosstab"/>.
/// </summary>
public sealed class TradeByContactMonthlyQueryHandler(IAppDbContext db)
    : IRequestHandler<TradeByContactMonthlyQuery, TradeByContactMonthlyDto>
{
    public async Task<TradeByContactMonthlyDto> Handle(TradeByContactMonthlyQuery request, CancellationToken cancellationToken)
    {
        var months = TradeMonthlyCrosstab.Columns(request.FiscalYear)
            ?? throw new NotFoundException(
                $"Fiscal year {request.FiscalYear} is outside the supported Bikram Sambat range.");

        var fromDate = months[0].FromDate;
        var toDate = months[^1].ToDate;

        var facts = await TradeLineReader.LoadAsync(
            db, request.OrganizationId, request.Side, fromDate, toDate, cancellationToken);

        var contactIds = facts.Select(x => x.ContactId).Distinct().ToList();

        var contactsQuery = db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id));

        if (request.ContactGroupId is { } groupId)
        {
            contactsQuery = contactsQuery.Where(x => x.GroupId == groupId);
        }

        var contacts = await contactsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.Pan, x.GroupId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var rows = facts
            .Where(x => contacts.ContainsKey(x.ContactId))
            .GroupBy(x => x.ContactId)
            .Select(g =>
            {
                var contact = contacts[g.Key];
                var monthly = TradeMonthlyCrosstab.Bucket(months, g.Select(x => (x.Date, x.NetAmount)));

                return new TradeByContactMonthlyRowDto(
                    contact.Id,
                    contact.Code,
                    contact.Name,
                    contact.Pan,
                    contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null,
                    monthly,
                    TradeMonthlyCrosstab.Quarters(monthly),
                    monthly.Sum());
            })
            .Where(x => x.Total != 0 || x.Monthly.Any(m => m != 0))
            .OrderBy(x => x.ContactCode, StringComparer.Ordinal)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        // Column totals span every filtered row, not the displayed page (phase-16c).
        var totalMonthly = new decimal[months.Count];
        foreach (var row in rows)
        {
            for (var i = 0; i < months.Count; i++)
            {
                totalMonthly[i] += row.Monthly[i];
            }
        }

        return new TradeByContactMonthlyDto(
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
