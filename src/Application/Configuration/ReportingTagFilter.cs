using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration;

/// <summary>
/// Shared narrowing step for a report whose rows come from a document type Phase 19 decision #1
/// confirmed carries Reporting Tags (Quotation, Invoice only). OR semantics across multiple selected
/// tags (decision #1's judgment call). Not a generic IQueryable helper with a captured Func selector
/// (CLAUDE.md's Phase 9 gotcha) -- this is a plain EF query with no delegate capture, so it
/// translates fine; still kept to exactly the (DocumentType, tag list) -&gt; matching-ids shape rather
/// than trying to generalize further.
/// </summary>
public static class ReportingTagFilter
{
    public static async Task<HashSet<Guid>?> ResolveMatchingDocumentIdsAsync(
        IAppDbContext db, DocumentType documentType, IReadOnlyList<Guid>? tagOptionIds, CancellationToken cancellationToken)
    {
        if (tagOptionIds is not { Count: > 0 })
        {
            return null;
        }

        var ids = await db.TransactionReportingTags
            .Where(t => t.DocumentType == documentType && tagOptionIds.Contains(t.TagOptionId))
            .Select(t => t.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
