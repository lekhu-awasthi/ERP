using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosDaySummary;

public sealed class GetPosDaySummaryQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPosDaySummaryQuery, PosDaySummaryDto>
{
    public async Task<PosDaySummaryDto> Handle(GetPosDaySummaryQuery request, CancellationToken cancellationToken)
    {
        if (request.LocationId is { } locationId
            && !await db.BillingLocations.AnyAsync(
                x => x.Id == locationId && x.OrganizationId == request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException("Billing location not found.");
        }

        var sales = await PosSalesReader.ForDayAsync(
            db, request.OrganizationId, request.Date, request.LocationId, cancellationToken);

        // The sessions that carried the day's sales -- the same invoice selection the figures came from.
        var daySales = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId
            && x.Channel == Domain.Sales.SalesChannel.Pos && x.Status == Domain.Sales.InvoiceStatus.Approved
            && x.Date == request.Date && x.PosSessionId != null);

        if (request.LocationId is { } onlyLocation)
        {
            daySales = daySales.Where(x => x.LocationId == onlyLocation);
        }

        var sessionIds = daySales.Select(x => x.PosSessionId!.Value).Distinct();

        var sessions = await (
                from session in db.PosSessions
                where session.OrganizationId == request.OrganizationId && sessionIds.Contains(session.Id)
                join user in db.Users on session.UserId equals user.Id
                orderby session.OpenedAt
                select new PosDaySessionDto(
                    session.Id, session.Code, session.BillingLocationId, session.UserId, user.FullName, session.Status,
                    session.CashDifference))
            .ToListAsync(cancellationToken);

        return new PosDaySummaryDto(request.Date, request.LocationId, sales, sessions);
    }
}
