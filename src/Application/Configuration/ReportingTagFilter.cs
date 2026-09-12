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
        IAppDbContext db, DocumentType documentType, IReadOnlyList<Guid>? tagOptionIds, CancellationToken cancellationToken,
        Guid? organizationId = null)
    {
        if (tagOptionIds is not { Count: > 0 })
        {
            return null;
        }

        var query = db.TransactionReportingTags
            .Where(t => t.DocumentType == documentType && tagOptionIds.Contains(t.TagOptionId));

        // Phase 36 -- the organization, when the caller has one to give. This helper narrows ids
        // that the caller then intersects with its own organization-filtered documents, so the
        // original had no leak; taking the organization anyway is phase-35b's rule that a shared
        // filter owns every condition rather than trusting each caller to re-add it.
        if (organizationId is { } organization)
        {
            query = query.Where(t => t.OrganizationId == organization);
        }

        var ids = await query
            .Select(t => t.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    /// <summary>
    /// Phase 36 -- the same narrowing for a report whose rows span <b>several</b> document types,
    /// keyed by the (type, id) pair rather than by id alone. The Journal report is the first: its
    /// rows are GL entries, and an entry points back at its document with exactly that pair.
    ///
    /// <para>Same OR semantics as the single-type overload above (phase-19 decision #1's judgment
    /// call): a document matches when it carries <i>any</i> of the selected options. The reference
    /// product's drawer groups the options by tag category, and what it does across two categories
    /// was not observable on a tenant with tagged data to hand -- so this deliberately keeps the
    /// semantics the Sales Register has had since phase 19 rather than inventing a second rule for
    /// a sibling report to disagree with.</para>
    /// </summary>
    public static async Task<HashSet<(DocumentType Type, Guid Id)>?> ResolveMatchingDocumentsAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyList<Guid>? tagOptionIds, CancellationToken cancellationToken)
    {
        if (tagOptionIds is not { Count: > 0 })
        {
            return null;
        }

        var matches = await db.TransactionReportingTags
            .Where(t => t.OrganizationId == organizationId && tagOptionIds.Contains(t.TagOptionId))
            .Select(t => new { t.DocumentType, t.DocumentId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return matches.Select(x => (x.DocumentType, x.DocumentId)).ToHashSet();
    }
}
