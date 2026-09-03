using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.DetailGeneralLedger;

public sealed class DetailGeneralLedgerQueryHandler(IAppDbContext db)
    : IRequestHandler<DetailGeneralLedgerQuery, PagedResult<DetailGeneralLedgerAccountDto>>
{
    public async Task<PagedResult<DetailGeneralLedgerAccountDto>> Handle(
        DetailGeneralLedgerQuery request, CancellationToken cancellationToken)
    {
        var openingCutoff = GlDateBoundary.EndOfDayUtc(request.FromDate.AddDays(-1));
        var periodFrom = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var periodTo = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var classification = await GlAccountClassification.LoadAsync(db, request.OrganizationId, cancellationToken);

        var openings = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId && entry.PostedAt <= openingCutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);
        var openingByAccount = openings.ToDictionary(x => x.AccountId, x => x.Net);

        var periodLines = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == request.OrganizationId
                  && entry.PostedAt >= periodFrom && entry.PostedAt <= periodTo
            select new
            {
                line.Id,
                line.GlJournalEntryId,
                line.AccountId,
                line.Debit,
                line.Credit,
                entry.PostedAt,
                entry.SourceDocumentType,
                entry.SourceDocumentId,
            })
            .ToListAsync(cancellationToken);

        // An account appears if it has an opening balance or any movement. One that has neither is
        // omitted: unlike the General Ledger Summary (which is a chart-of-accounts rollup and lists
        // every account), this report is a ledger, and a ledger page with no opening figure and no
        // postings says nothing.
        var accountIds = openingByAccount.Where(x => x.Value != 0m).Select(x => x.Key)
            .Concat(periodLines.Select(x => x.AccountId))
            .Distinct()
            .Where(id => request.AccountId is null || id == request.AccountId)
            .ToList();

        var orderedAccounts = accountIds
            .Select(id => classification.For(id))
            .Where(a => a is not null)
            .Select(a => a!)
            .OrderBy(a => a.AccountCode)
            .ToList();

        var paged = request.ExportAll
            ? ((IReadOnlyList<GlAccountClassification.AccountFacts>)orderedAccounts).ToUnpagedResult()
            : ((IReadOnlyList<GlAccountClassification.AccountFacts>)orderedAccounts).ToPagedResult(request.Page, request.PageSize);

        if (paged.Items.Count == 0)
        {
            return new PagedResult<DetailGeneralLedgerAccountDto>([], paged.Page, paged.PageSize, paged.TotalCount);
        }

        var pageAccountIds = paged.Items.Select(a => a.AccountId).ToHashSet();
        var pageLines = periodLines.Where(x => pageAccountIds.Contains(x.AccountId)).ToList();

        // The Description column is the contra side of each posting, so the page needs every line of
        // each entry it touches -- including lines against accounts outside this page.
        var entryIds = pageLines.Select(x => x.GlJournalEntryId).Distinct().ToList();
        var siblingLines = await db.GlLines
            .Where(x => entryIds.Contains(x.GlJournalEntryId))
            .Select(x => new SiblingLine(x.Id, x.GlJournalEntryId, x.AccountId))
            .ToListAsync(cancellationToken);
        var siblingsByEntry = siblingLines.GroupBy(x => x.GlJournalEntryId).ToDictionary(g => g.Key, g => g.ToList());

        var documents = await GlSourceDocumentResolver.LoadAsync(
            db,
            request.OrganizationId,
            [.. pageLines.Select(x => (x.SourceDocumentType, x.SourceDocumentId)).Distinct()],
            cancellationToken);

        var linesByAccount = pageLines.GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => g.ToList());

        var sections = paged.Items.Select(account =>
        {
            var opening = openingByAccount.GetValueOrDefault(account.AccountId);
            var running = opening;

            var ordered = linesByAccount.GetValueOrDefault(account.AccountId, [])
                .OrderBy(x => x.PostedAt)
                .ThenBy(x => x.SourceDocumentId)
                .ThenBy(x => x.Id)
                .ToList();

            var rows = new List<DetailGeneralLedgerRowDto>(ordered.Count);
            foreach (var line in ordered)
            {
                running += line.Debit - line.Credit;
                var document = documents.For(line.SourceDocumentType, line.SourceDocumentId);
                rows.Add(new DetailGeneralLedgerRowDto(
                    DateOnly.FromDateTime(line.PostedAt.UtcDateTime),
                    line.SourceDocumentType,
                    line.SourceDocumentId,
                    document?.Code,
                    document?.Reference,
                    ContraDescription(siblingsByEntry, classification, line.GlJournalEntryId, line.Id, account.AccountId),
                    line.Debit,
                    line.Credit,
                    GlBalanceMarker.Magnitude(running),
                    GlBalanceMarker.For(running),
                    document?.Direction));
            }

            return new DetailGeneralLedgerAccountDto(
                account.AccountId,
                account.AccountCode,
                account.AccountName,
                GlBalanceMarker.Magnitude(opening),
                GlBalanceMarker.For(opening),
                rows,
                ordered.Sum(x => x.Debit),
                ordered.Sum(x => x.Credit),
                GlBalanceMarker.Magnitude(running),
                GlBalanceMarker.For(running));
        }).ToList();

        return new PagedResult<DetailGeneralLedgerAccountDto>(sections, paged.Page, paged.PageSize, paged.TotalCount);
    }

    /// <summary>
    /// The <i>other</i> accounts touched by the same journal entry, comma-separated -- the derivable
    /// half of the live report's Description column (see the query's doc comment for why the
    /// narration half is not). Every line of this account is excluded, not just this one row, so an
    /// entry that both debits and credits the same account does not name itself as its own contra.
    /// Returns null rather than an empty string when there is nothing to name.
    /// </summary>
    private static string? ContraDescription(
        Dictionary<Guid, List<SiblingLine>> siblingsByEntry,
        GlAccountClassification classification,
        Guid entryId,
        Guid thisLineId,
        Guid thisAccountId)
    {
        if (!siblingsByEntry.TryGetValue(entryId, out var siblings))
        {
            return null;
        }

        var names = siblings
            .Where(x => x.Id != thisLineId && x.AccountId != thisAccountId)
            .Select(x => classification.For(x.AccountId)?.AccountName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private sealed record SiblingLine(Guid Id, Guid GlJournalEntryId, Guid AccountId);
}
