using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosDayReport;

/// <summary>
/// A drawer that worked in the period: opened in it, or took a sale or a refund in it. The four cash
/// figures are the session's own; <paramref name="CashDifference"/> is null while it is open.
/// </summary>
public sealed record PosDayReportSessionDto(
    Guid Id,
    string Code,
    Guid LocationId,
    string LocationName,
    Guid UserId,
    string UserName,
    PosSessionStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal OpeningFloat,
    decimal CashIn,
    decimal CashOut,
    decimal? CountedCash,
    decimal? CashDifference);

/// <summary>
/// Phase 66 -- the Day Report: one period's till takings. <paramref name="Sales"/> and
/// <paramref name="Series"/> come from <see cref="PosSalesReader.ForPeriodAsync"/>, the reader every session
/// and X/Z figure comes from, so a day and its sessions cannot disagree (the vendor's defect 7).
/// </summary>
/// <param name="VoidedSales">Till sales dated in the period and voided since: counted, never summed --
/// a void takes a sale out of every figure (Decision C), and this line is where it stays visible.</param>
/// <param name="VoidedOrders">Restaurant orders dated in the period and voided whole.</param>
public sealed record PosDayReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? LocationId,
    PosSalesSummaryDto Sales,
    PosPaymentBreakdownDto Payments,
    PosSalesBucket Bucket,
    IReadOnlyList<PosSalesPointDto> Series,
    int VoidedSales,
    int VoidedRefunds,
    int VoidedOrders,
    IReadOnlyList<PosDayReportSessionDto> Sessions);

/// <summary>
/// Phase 66 -- the Day Report over a period, at one location or all of them; phase 61's day summary
/// generalised from one date to a range rather than kept beside it, so the till's money has one door.
///
/// <para>Admin-only through <see cref="PermissionKeys.PosSessionViewAll"/>, the key phase 59 Decision J
/// and phase 61 derived for "the day report": it sums every cashier's drawer, the figures that say whose
/// drawer was short. Organization-wide, like the key, so it takes no report location scope.</para>
/// </summary>
public sealed record GetPosDayReportQuery(Guid OrganizationId, DateOnly FromDate, DateOnly ToDate, Guid? LocationId = null)
    : IRequest<PosDayReportDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionViewAll;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class GetPosDayReportQueryValidator : AbstractValidator<GetPosDayReportQuery>
{
    public GetPosDayReportQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.");
        RuleFor(x => x.ToDate)
            .Must((request, to) => to.DayNumber - request.FromDate.DayNumber < PosReportPeriod.MaxDays)
            .WithMessage($"A POS report covers at most {PosReportPeriod.MaxDays} days.");
    }
}

/// <summary>Phase 66 -- the longest period a POS report's series is drawn over: a year, one point a day.</summary>
public static class PosReportPeriod
{
    public const int MaxDays = 366;
}

public sealed class GetPosDayReportQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPosDayReportQuery, PosDayReportDto>
{
    public async Task<PosDayReportDto> Handle(GetPosDayReportQuery request, CancellationToken cancellationToken)
    {
        if (request.LocationId is { } locationId
            && !await db.BillingLocations.AnyAsync(
                x => x.Id == locationId && x.OrganizationId == request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException("Billing location not found.");
        }

        var period = await PosSalesReader.ForPeriodAsync(
            db, request.OrganizationId, request.FromDate, request.ToDate, request.LocationId, cancellationToken);

        // The documents behind the figures -- the reader's own selections -- and the voids beside them.
        var sales = PosSalesReader.TillSales(db, request.OrganizationId)
            .Where(x => x.Date >= request.FromDate && x.Date <= request.ToDate);
        var refunds = PosSalesReader.TillRefunds(db, request.OrganizationId)
            .Where(x => x.Date >= request.FromDate && x.Date <= request.ToDate);
        var voidedSales = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId
            && x.Channel == SalesChannel.Pos && x.Status == InvoiceStatus.Void
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        var voidedRefunds = db.CreditNotes.Where(x => x.OrganizationId == request.OrganizationId
            && x.Channel == SalesChannel.Pos && x.Status == CreditNoteStatus.Void
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        var voidedOrders = db.PosOrders.Where(x => x.OrganizationId == request.OrganizationId
            && x.Status == PosOrderStatus.Voided && x.Date >= request.FromDate && x.Date <= request.ToDate);
        var sessions = db.PosSessions.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.LocationId is { } onlyLocation)
        {
            sales = sales.Where(x => x.LocationId == onlyLocation);
            refunds = refunds.Where(x => x.LocationId == onlyLocation);
            voidedSales = voidedSales.Where(x => x.LocationId == onlyLocation);
            voidedRefunds = voidedRefunds.Where(x => x.LocationId == onlyLocation);
            voidedOrders = voidedOrders.Where(x => x.BillingLocationId == onlyLocation);
            sessions = sessions.Where(x => x.BillingLocationId == onlyLocation);
        }

        // A session belongs to the period when it was opened in it (Nepal days) or worked in it. Phase 61
        // section 5: a session that spans midnight puts its sales on two days, so it appears on both.
        var openedFrom = new DateTimeOffset(request.FromDate.ToDateTime(TimeOnly.MinValue), Domain.Common.NepalTime.Offset);
        var openedBefore = new DateTimeOffset(request.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), Domain.Common.NepalTime.Offset);
        var workedIn = sales.Where(x => x.PosSessionId != null).Select(x => x.PosSessionId!.Value)
            .Concat(refunds.Where(x => x.PosSessionId != null).Select(x => x.PosSessionId!.Value));

        var periodSessions = await sessions
            .Include(x => x.CashMovements)
            .Where(x => (x.OpenedAt >= openedFrom && x.OpenedAt < openedBefore) || workedIn.Contains(x.Id))
            .OrderBy(x => x.OpenedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var userIds = periodSessions.Select(x => x.UserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
        var locationIds = periodSessions.Select(x => x.BillingLocationId).Distinct().ToList();
        var locationNames = await db.BillingLocations
            .Where(x => x.OrganizationId == request.OrganizationId && locationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return new PosDayReportDto(
            request.FromDate,
            request.ToDate,
            request.LocationId,
            period.Summary,
            PosSalesReader.Payments(period.Summary),
            period.Bucket,
            period.Series,
            await voidedSales.CountAsync(cancellationToken),
            await voidedRefunds.CountAsync(cancellationToken),
            await voidedOrders.CountAsync(cancellationToken),
            [
                .. periodSessions.Select(x => new PosDayReportSessionDto(
                    x.Id,
                    x.Code,
                    x.BillingLocationId,
                    locationNames.GetValueOrDefault(x.BillingLocationId, ""),
                    x.UserId,
                    userNames.GetValueOrDefault(x.UserId, ""),
                    x.Status,
                    x.OpenedAt,
                    x.ClosedAt,
                    x.OpeningFloat,
                    x.CashMovements.Where(m => m.Direction == PosCashMovementDirection.In).Sum(m => m.Amount),
                    x.CashMovements.Where(m => m.Direction == PosCashMovementDirection.Out).Sum(m => m.Amount),
                    x.CountedCash,
                    x.CashDifference)),
            ]);
    }
}
