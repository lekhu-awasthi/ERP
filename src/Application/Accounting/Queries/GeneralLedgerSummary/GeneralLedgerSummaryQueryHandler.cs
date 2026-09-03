using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Trees;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.GeneralLedgerSummary;

public sealed class GeneralLedgerSummaryQueryHandler(IAppDbContext db, ITreeQuery<AccountGroup> treeQuery)
    : IRequestHandler<GeneralLedgerSummaryQuery, PagedResult<GeneralLedgerSummaryRowDto>>
{
    public async Task<PagedResult<GeneralLedgerSummaryRowDto>> Handle(
        GeneralLedgerSummaryQuery request, CancellationToken cancellationToken)
    {
        var openingCutoff = GlDateBoundary.EndOfDayUtc(request.FromDate.AddDays(-1));
        var periodFrom = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var periodTo = GlDateBoundary.EndOfDayUtc(request.ToDate);

        // Net position strictly before the period -- the opening balance the live report prints.
        var openings = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId && entry.PostedAt <= openingCutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);
        var openingByAccount = openings.ToDictionary(x => x.AccountId, x => x.Net);

        var movements = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId
                  && entry.PostedAt >= periodFrom && entry.PostedAt <= periodTo
            group line by line.AccountId into g
            select new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);
        var movementByAccount = movements.ToDictionary(x => x.AccountId, x => (x.Debit, x.Credit));

        var classification = await GlAccountClassification.LoadAsync(db, request.OrganizationId, cancellationToken);

        // The Group filter matches the chosen group and everything under it -- a group filter that
        // ignored subgroups would silently exclude the accounts a user filtering by "Current Assets"
        // most expects to see. Reuses Phase 8a's own subtree helper rather than a second walk.
        HashSet<Guid>? groupIds = null;
        if (request.GroupId is { } groupId)
        {
            var subtree = await treeQuery.GetSubtreeIdsAsync(request.OrganizationId, groupId, cancellationToken);
            groupIds = subtree.ToHashSet();
        }

        var rows = classification.Accounts
            .Where(a => request.AccountId is null || a.AccountId == request.AccountId)
            .Where(a => groupIds is null || groupIds.Contains(a.GroupId))
            .Select(a =>
            {
                var opening = openingByAccount.GetValueOrDefault(a.AccountId);
                var (debit, credit) = movementByAccount.GetValueOrDefault(a.AccountId);
                var closing = opening + debit - credit;
                return new GeneralLedgerSummaryRowDto(
                    a.AccountId,
                    a.AccountCode,
                    a.AccountName,
                    a.ParentGroupName,
                    a.GroupTypeName,
                    a.RootType,
                    GlBalanceMarker.Magnitude(opening),
                    GlBalanceMarker.For(opening),
                    debit,
                    credit,
                    GlBalanceMarker.Magnitude(closing),
                    GlBalanceMarker.For(closing));
            })
            .OrderBy(r => r.AccountCode)
            .ToList();

        return request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);
    }
}
