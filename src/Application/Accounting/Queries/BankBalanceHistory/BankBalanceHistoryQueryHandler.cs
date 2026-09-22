using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.BankBalanceHistory;

/// <summary>
/// See <see cref="BankBalanceHistoryQuery"/> for the shape and the calendar decision. The sequence
/// is the same on both sides and is the only way a cumulative series can be built without being
/// linear in the history per point: <b>one</b> store-side sum for everything before the window, then
/// the window's own movements bucketed by day, then a running total.
/// </summary>
public sealed class BankBalanceHistoryQueryHandler(IAppDbContext db)
    : IRequestHandler<BankBalanceHistoryQuery, BankBalanceHistoryDto>
{
    public async Task<BankBalanceHistoryDto> Handle(
        BankBalanceHistoryQuery request, CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .Where(x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Code, x.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            throw new NotFoundException("Bank account not found.");
        }

        // Nepal's wall clock for "today", exactly as BankReconciliationReportQueryHandler does it --
        // the two screens open on the same day or the chart's last point would not be the figure
        // printed above it.
        var asOf = request.AsOfDate ?? NepalTime.LocalDate(DateTimeOffset.UtcNow);
        var days = Math.Clamp(request.Days, 1, BankBalanceHistoryQuery.MaxDays);
        var from = asOf.AddDays(-(days - 1));
        var dayBefore = from.AddDays(-1);

        var reader = new BankBookTransactionReader(db);

        var bookOpening = await reader.SumAsync(
            new BookMovementFilter(request.OrganizationId, request.BankAccountId, ToDate: dayBefore),
            cancellationToken);

        var bookByDay = (await reader.DailyNetAsync(
                new BookMovementFilter(
                    request.OrganizationId, request.BankAccountId, FromDate: from, ToDate: asOf),
                cancellationToken))
            .ToDictionary(x => x.Day, x => x.Signed);

        // The bank side is summed in memory for the reason the report records: StatementAmount
        // reaches the store through a value converter, so the provider knows it only as a decimal
        // column and `x.Amount.Signed` cannot be translated. Only the column comes back.
        var statementLines = db.BankStatementLines.Where(
            x => x.OrganizationId == request.OrganizationId
                 && x.BankAccountId == request.BankAccountId);

        var bankOpening = BankMovementTotal.OfStatementLines(
            await statementLines.Where(x => x.Date <= dayBefore)
                .Select(x => x.Amount)
                .ToListAsync(cancellationToken));

        var bankRows = await statementLines
            .Where(x => x.Date >= from && x.Date <= asOf)
            .Select(x => new { x.Date, x.Amount })
            .ToListAsync(cancellationToken);

        var bankByDay = bankRows
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => BankMovementTotal.OfStatementLines(g.Select(x => x.Amount)).Signed);

        var points = new List<BankBalanceHistoryPoint>(days);
        var bookRunning = bookOpening.Signed;
        var bankRunning = bankOpening.Signed;

        // Every day of the window, including the quiet ones -- see the DTO for why a gap would be
        // read as a change.
        for (var day = from; day <= asOf; day = day.AddDays(1))
        {
            bookRunning += bookByDay.GetValueOrDefault(day);
            bankRunning += bankByDay.GetValueOrDefault(day);

            points.Add(new BankBalanceHistoryPoint(
                day, bookRunning, bankRunning, bankRunning - bookRunning));
        }

        return new BankBalanceHistoryDto(
            request.BankAccountId, account.Code, account.Name, from, asOf, points);
    }
}
