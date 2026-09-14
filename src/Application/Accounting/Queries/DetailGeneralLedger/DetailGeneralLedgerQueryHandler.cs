using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.DetailGeneralLedger;

/// <summary>
/// Phase 42 -- the row-paged shape. What this handler no longer does is materialise every posting in
/// the period: the old one read all 210,006 GL lines to build sections it then sliced by account,
/// which is where both the 9.5 s and the 59.5 MB came from. Now the period is touched only by
/// aggregates (one row per account), and the postings themselves are fetched for the requested page
/// alone -- <c>JournalReportQueryHandler</c>'s shape, with the account ordering standing in for the
/// entry keys.
/// </summary>
public sealed class DetailGeneralLedgerQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<DetailGeneralLedgerQuery, PagedResult<DetailGeneralLedgerAccountDto>>
{
    public async Task<PagedResult<DetailGeneralLedgerAccountDto>> Handle(
        DetailGeneralLedgerQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);
        var entries = GlEntryLocations.ForReport(db, request.OrganizationId, request.LocationId, reportLocations);

        var openingCutoff = GlDateBoundary.EndOfDayUtc(request.FromDate.AddDays(-1));
        var periodFrom = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var periodTo = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var classification = await GlAccountClassification.LoadAsync(db, request.OrganizationId, cancellationToken);

        var openings = await (
            from line in db.GlLines
            join entry in entries on line.GlJournalEntryId equals entry.Id
            where entry.PostedAt <= openingCutoff
            group line by line.AccountId into g
            select new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken);
        var openingByAccount = openings.ToDictionary(x => x.AccountId, x => x.Net);

        // The period, as one row per account rather than one row per posting. This is the whole
        // difference: Count is what makes the row paging arithmetic possible, and Debit/Credit are
        // the section's period totals -- the figures the live Closing Balance row prints in its
        // Debit and Credit cells, which phase 16c's footer rule says must cover the whole filtered
        // set and not the page.
        var periodQuery =
            from line in db.GlLines
            join entry in entries on line.GlJournalEntryId equals entry.Id
            where entry.PostedAt >= periodFrom && entry.PostedAt <= periodTo
            select line;

        var periodTotals = await periodQuery
            .GroupBy(x => x.AccountId)
            .Select(g => new AccountPeriodTotals(
                g.Key,
                g.Count(),
                g.Sum(x => x.Debit),
                g.Sum(x => x.Credit)))
            .ToListAsync(cancellationToken);
        var periodByAccount = periodTotals.ToDictionary(x => x.AccountId);

        // An account appears if it has an opening balance or any movement. One that has neither is
        // omitted: unlike the General Ledger Summary (which is a chart-of-accounts rollup and lists
        // every account), this report is a ledger, and a ledger page with no opening figure and no
        // postings says nothing.
        var accountIds = openingByAccount.Where(x => x.Value != 0m).Select(x => x.Key)
            .Concat(periodTotals.Select(x => x.AccountId))
            .Distinct()
            .Where(id => request.AccountId is null || id == request.AccountId)
            .ToList();

        var orderedAccounts = accountIds
            .Select(id => classification.For(id))
            .Where(a => a is not null)
            .Select(a => a!)
            .OrderBy(a => a.AccountCode)
            .ToList();

        var totalRows = orderedAccounts.Sum(a => periodByAccount.GetValueOrDefault(a.AccountId)?.Count ?? 0);

        // ExportAll asks for the whole filtered set and the export path is the one caller that can
        // take it -- same filters, same permission gate, paging ignored (PagedResultExtensions).
        var skip = request.ExportAll ? 0 : (request.Page - 1) * request.PageSize;
        var take = request.ExportAll ? int.MaxValue : request.PageSize;
        var page = request.ExportAll ? 1 : request.Page;
        var pageSize = request.ExportAll ? Math.Max(totalRows, 1) : request.PageSize;

        if (totalRows == 0 || skip >= totalRows)
        {
            // An account with an opening balance and no postings is still a section the live report
            // prints, so a period with no movement at all is not necessarily an empty report -- but
            // with the row as the page unit there is nothing to put on a page, which is the honest
            // consequence of the decision recorded on the query.
            return new PagedResult<DetailGeneralLedgerAccountDto>([], page, pageSize, totalRows);
        }

        var sections = new List<DetailGeneralLedgerAccountDto>();
        var remaining = take;
        var cursor = 0;

        foreach (var account in orderedAccounts)
        {
            if (remaining <= 0)
            {
                break;
            }

            var totals = periodByAccount.GetValueOrDefault(account.AccountId);
            var accountRows = totals?.Count ?? 0;
            if (accountRows == 0 || cursor + accountRows <= skip)
            {
                cursor += accountRows;
                continue;
            }

            var rowsBefore = Math.Max(skip - cursor, 0);
            var wanted = Math.Min(accountRows - rowsBefore, remaining);

            var section = await LoadSectionAsync(
                db, request, classification, periodQuery, entries, account,
                openingByAccount.GetValueOrDefault(account.AccountId),
                totals?.Debit ?? 0m,
                totals?.Credit ?? 0m,
                rowsBefore, wanted, accountRows, cancellationToken);

            sections.Add(section);
            cursor += accountRows;
            remaining -= wanted;
        }

        return new PagedResult<DetailGeneralLedgerAccountDto>(sections, page, pageSize, totalRows);
    }

    /// <summary>
    /// One section's rows, taken with SQL's own OFFSET/FETCH so a page inside a 50,000-posting
    /// control account costs the page and not the account.
    /// </summary>
    private static async Task<DetailGeneralLedgerAccountDto> LoadSectionAsync(
        IAppDbContext db,
        DetailGeneralLedgerQuery request,
        GlAccountClassification classification,
        IQueryable<Domain.Accounting.GlLine> periodQuery,
        IQueryable<Domain.Accounting.GlJournalEntry> entries,
        GlAccountClassification.AccountFacts account,
        decimal opening,
        decimal periodDebit,
        decimal periodCredit,
        int rowsBefore,
        int wanted,
        int accountRows,
        CancellationToken cancellationToken)
    {
        var accountId = account.AccountId;

        // The ordering has to be total and has to be the same in both statements below, or the
        // running balance carried across a page boundary would be the sum of a different set of
        // rows than the ones the page shows.
        //
        // The projection into LedgerLine is deliberately NOT part of this query. Summing over a
        // query that has already projected into a record makes EF try to construct the record
        // server-side to read one of its properties, which it cannot translate -- and the
        // InMemory provider evaluates it in C# instead, so every handler test passes while the
        // endpoint returns 500 on any page after the first (phase 34b's gotcha, phase 38's in
        // another costume). Both statements below therefore read the raw columns.
        var orderedLines =
            from line in periodQuery
            where line.AccountId == accountId
            join entry in entries on line.GlJournalEntryId equals entry.Id
            orderby entry.PostedAt, entry.SourceDocumentId, line.Id
            select new { Line = line, Entry = entry };

        // What the account had moved by before this page's first row. Zero for a section that
        // starts on this page; a TOP(n) subquery otherwise, never a fetch of the skipped rows.
        var carried = rowsBefore == 0
            ? 0m
            : await orderedLines.Take(rowsBefore)
                .SumAsync(x => x.Line.Debit - x.Line.Credit, cancellationToken);

        var pageLines = await orderedLines
            .Skip(rowsBefore)
            .Take(wanted)
            .Select(x => new LedgerLine(
                x.Line.Id, x.Line.GlJournalEntryId, x.Line.Debit, x.Line.Credit,
                x.Entry.PostedAt, x.Entry.SourceDocumentType, x.Entry.SourceDocumentId))
            .ToListAsync(cancellationToken);

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

        var running = opening + carried;
        var rows = new List<DetailGeneralLedgerRowDto>(pageLines.Count);

        foreach (var line in pageLines)
        {
            running += line.Debit - line.Credit;
            var document = documents.For(line.SourceDocumentType, line.SourceDocumentId);
            rows.Add(new DetailGeneralLedgerRowDto(
                DateOnly.FromDateTime(line.PostedAt.UtcDateTime),
                line.SourceDocumentType,
                line.SourceDocumentId,
                document?.Code,
                document?.Reference,
                ContraDescription(siblingsByEntry, classification, line.GlJournalEntryId, line.Id, accountId),
                line.Debit,
                line.Credit,
                GlBalanceMarker.Magnitude(running),
                GlBalanceMarker.For(running),
                document?.Direction));
        }

        // Closing is the account's figure over the whole period, not the page's -- see the query's
        // doc comment. Derived from the period totals so a partial section still prints the same
        // Closing Balance row as a whole one.
        var closing = opening + periodDebit - periodCredit;

        return new DetailGeneralLedgerAccountDto(
            accountId,
            account.AccountCode,
            account.AccountName,
            GlBalanceMarker.Magnitude(opening),
            GlBalanceMarker.For(opening),
            rows,
            periodDebit,
            periodCredit,
            GlBalanceMarker.Magnitude(closing),
            GlBalanceMarker.For(closing),
            rowsBefore,
            accountRows - rowsBefore - rows.Count);
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

    /// <summary>One account's period, as a single row: the count that makes the row paging
    /// arithmetic possible and the two totals the Closing Balance row prints.</summary>
    private sealed record AccountPeriodTotals(Guid AccountId, int Count, decimal Debit, decimal Credit);

    private sealed record LedgerLine(
        Guid Id,
        Guid GlJournalEntryId,
        decimal Debit,
        decimal Credit,
        DateTimeOffset PostedAt,
        DocumentType SourceDocumentType,
        Guid SourceDocumentId);
}
