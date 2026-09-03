using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.JournalReport;

public sealed class JournalReportQueryHandler(IAppDbContext db)
    : IRequestHandler<JournalReportQuery, PagedResult<JournalReportEntryDto>>
{
    public async Task<PagedResult<JournalReportEntryDto>> Handle(
        JournalReportQuery request, CancellationToken cancellationToken)
    {
        var fromUtc = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var toUtc = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var entryQuery = db.GlJournalEntries
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.PostedAt >= fromUtc && x.PostedAt <= toUtc);

        if (request.DocumentType is { } documentType)
        {
            entryQuery = entryQuery.Where(x => x.SourceDocumentType == documentType);
        }

        var entries = await entryQuery
            .Select(x => new { x.Id, x.PostedAt, x.SourceDocumentType, x.SourceDocumentId })
            .ToListAsync(cancellationToken);

        // Newest first, as the live report reads; Id makes the order total so paging is stable.
        var ordered = entries
            .OrderByDescending(x => x.PostedAt)
            .ThenBy(x => x.Id)
            .ToList();

        // Paged at document granularity, which is both what the live report does and the only
        // paging that keeps a block's own Total row correct -- see the query's doc comment.
        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);

        if (paged.Items.Count == 0)
        {
            return new PagedResult<JournalReportEntryDto>([], paged.Page, paged.PageSize, paged.TotalCount);
        }

        var entryIds = paged.Items.Select(x => x.Id).ToList();
        var lines = await db.GlLines
            .Where(x => entryIds.Contains(x.GlJournalEntryId))
            .Select(x => new { x.Id, x.GlJournalEntryId, x.AccountId, x.Debit, x.Credit })
            .ToListAsync(cancellationToken);
        var linesByEntry = lines.GroupBy(x => x.GlJournalEntryId).ToDictionary(g => g.Key, g => g.ToList());

        var classification = await GlAccountClassification.LoadAsync(db, request.OrganizationId, cancellationToken);
        var documentKeys = paged.Items
            .Select(x => (x.SourceDocumentType, x.SourceDocumentId))
            .Distinct()
            .ToList();
        var documents = await GlSourceDocumentResolver.LoadAsync(
            db, request.OrganizationId, documentKeys, cancellationToken);

        var rows = paged.Items.Select(entry =>
        {
            var entryLines = linesByEntry.GetValueOrDefault(entry.Id, []);
            var document = documents.For(entry.SourceDocumentType, entry.SourceDocumentId);
            var lineDtos = entryLines
                .Select(line =>
                {
                    var account = classification.For(line.AccountId);
                    return new JournalReportLineDto(
                        line.AccountId,
                        account?.AccountCode ?? string.Empty,
                        account?.AccountName ?? string.Empty,
                        line.Debit,
                        line.Credit);
                })
                .OrderByDescending(x => x.Debit)
                .ThenBy(x => x.AccountCode)
                .ToList();

            return new JournalReportEntryDto(
                entry.Id,
                DateOnly.FromDateTime(entry.PostedAt.UtcDateTime),
                entry.SourceDocumentType,
                entry.SourceDocumentId,
                document?.Code,
                document?.Reference,
                document?.Direction,
                lineDtos,
                lineDtos.Sum(x => x.Debit),
                lineDtos.Sum(x => x.Credit));
        }).ToList();

        return new PagedResult<JournalReportEntryDto>(rows, paged.Page, paged.PageSize, paged.TotalCount);
    }
}
