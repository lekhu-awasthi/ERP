using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosSessions;

/// <summary>
/// One drawer. <paramref name="Sales"/> and <paramref name="Refunds"/> are the session's grand totals as
/// the X/Z report reads them (<see cref="PosSalesReader"/>'s selection: approved till documents of the
/// session); <paramref name="ExpectedCash"/>, <paramref name="CountedCash"/> and
/// <paramref name="CashDifference"/> are frozen at the close, and null while it is open.
/// </summary>
public sealed record PosSessionRowDto(
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
    int SalesCount,
    decimal Sales,
    decimal Refunds,
    decimal? ExpectedCash,
    decimal? CountedCash,
    decimal? CashDifference);

/// <summary>
/// Phase 66 -- the POS Sessions list (phase 61 section 5: "phase 66's reports are the natural home for a
/// list"): every drawer opened in a period, at one location or all, open or closed, with whose it was and
/// whether its count came up short. Each row opens the session's own page (its X or Z report).
///
/// <para><see cref="PermissionKeys.PosSessionViewAll"/> (Admin-only): "list every session" is in the key's
/// own definition. The period is the Nepal day each drawer was <i>opened</i>; a drawer that works past
/// midnight is listed once, on the day it opened.</para>
/// </summary>
public sealed record ListPosSessionsQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? LocationId = null,
    PosSessionStatus? Status = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false,
    // The list's search box (NFR-6.1): a session code, or the cashier's name.
    string? Search = null)
    : IRequest<PagedResult<PosSessionRowDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature,
        ISearchableQuery
{
    public string PermissionKey => PermissionKeys.PosSessionViewAll;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class ListPosSessionsQueryValidator : AbstractValidator<ListPosSessionsQuery>
{
    public ListPosSessionsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.");
        RuleFor(x => x.ToDate)
            .Must((request, to) => to.DayNumber - request.FromDate.DayNumber < PosReportPeriod.MaxDays)
            .WithMessage($"A POS report covers at most {PosReportPeriod.MaxDays} days.");
        RuleFor(x => x.Status).IsInEnum();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
    }
}

public sealed class ListPosSessionsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPosSessionsQuery, PagedResult<PosSessionRowDto>>
{
    public async Task<PagedResult<PosSessionRowDto>> Handle(ListPosSessionsQuery request, CancellationToken cancellationToken)
    {
        if (request.LocationId is { } locationId
            && !await db.BillingLocations.AnyAsync(
                x => x.Id == locationId && x.OrganizationId == request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException("Billing location not found.");
        }

        var openedFrom = new DateTimeOffset(request.FromDate.ToDateTime(TimeOnly.MinValue), NepalTime.Offset);
        var openedBefore = new DateTimeOffset(request.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), NepalTime.Offset);

        var query = db.PosSessions.Where(x => x.OrganizationId == request.OrganizationId
            && x.OpenedAt >= openedFrom && x.OpenedAt < openedBefore);

        if (request.LocationId is { } onlyLocation)
        {
            query = query.Where(x => x.BillingLocationId == onlyLocation);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        // Single-argument Contains, which SQL Server turns into a LIKE (known-gotchas: never a static
        // matcher inside the predicate).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Code.Contains(term)
                || db.Users.Any(u => u.Id == x.UserId && u.FullName.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return new PagedResult<PosSessionRowDto>([], request.Page, request.PageSize, 0);
        }

        var ordered = query.OrderByDescending(x => x.OpenedAt).ThenBy(x => x.Code);
        var sessions = request.ExportAll
            ? await ordered.ToListAsync(cancellationToken)
            : await ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);

        var ids = sessions.Select(x => x.Id).ToList();

        // The page's takings, by the reader's own selections, summed per session store-side.
        var sales = PosSalesReader.TillSales(db, request.OrganizationId)
            .Where(x => x.PosSessionId != null && ids.Contains(x.PosSessionId.Value));
        var saleCounts = await sales
            .GroupBy(x => x.PosSessionId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count(), RoundOff = g.Sum(x => x.RoundOff) })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var saleLines = await (
                from line in db.InvoiceLines
                join invoice in sales on line.InvoiceId equals invoice.Id
                group line by invoice.PosSessionId!.Value into g
                select new { Id = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.Id, x => x.Total, cancellationToken);

        var refunds = PosSalesReader.TillRefunds(db, request.OrganizationId)
            .Where(x => x.PosSessionId != null && ids.Contains(x.PosSessionId.Value));
        var refundRoundOff = await refunds
            .GroupBy(x => x.PosSessionId!.Value)
            .Select(g => new { Id = g.Key, RoundOff = g.Sum(x => x.RoundOff) })
            .ToDictionaryAsync(x => x.Id, x => x.RoundOff, cancellationToken);
        var refundLines = await (
                from line in db.CreditNoteLines
                join note in refunds on line.CreditNoteId equals note.Id
                group line by note.PosSessionId!.Value into g
                select new { Id = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.Id, x => x.Total, cancellationToken);

        var userIds = sessions.Select(x => x.UserId).Distinct().ToList();
        var userNames = await db.Users.Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);
        var locationNames = await db.BillingLocations.Where(x => x.OrganizationId == request.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var rows = sessions.Select(x =>
        {
            var counted = saleCounts.GetValueOrDefault(x.Id);
            return new PosSessionRowDto(
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
                counted?.Count ?? 0,
                saleLines.GetValueOrDefault(x.Id) + (counted?.RoundOff ?? 0m),
                refundLines.GetValueOrDefault(x.Id) + refundRoundOff.GetValueOrDefault(x.Id),
                x.ExpectedCash,
                x.CountedCash,
                x.CashDifference);
        }).ToList();

        return request.ExportAll
            ? new PagedResult<PosSessionRowDto>(rows, 1, Math.Max(rows.Count, 1), totalCount)
            : new PagedResult<PosSessionRowDto>(rows, request.Page, request.PageSize, totalCount);
    }
}
