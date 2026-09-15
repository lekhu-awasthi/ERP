using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration;

/// <summary>
/// Shared narrowing step for a report filtered by Reporting Tags.
///
/// <para><b>The semantics, measured live on Moonbeam 2026-09-15 (phase 44).</b> A document matches
/// when it carries <i>any</i> of the options selected <b>within</b> a tag category, and it must
/// match in <b>every</b> category that has a selection. Standard faceted filtering: OR within,
/// AND across.</para>
///
/// <para><b>This replaces the rule phases 19 and 36 carried</b>, which was "any of everything
/// selected" -- OR across categories as well as within. That was never observed; phase 19 chose it
/// as a judgment call and phase 36 kept it explicitly because the tenant it read had no second
/// category's worth of tagged data. Phase 44 found a tenant with six categories and measured it:
/// on Inventory Position, BUSINESS{BUSINESS} returned 3 products and BUSINESS{BUSINESS} +
/// SERVICE{sdfsdfds} returned <b>2</b> -- a strict subset, which OR cannot produce. Adding a second
/// option inside BUSINESS took the same filter from 3 to 4, which AND cannot produce. Both axes
/// pinned by <c>ReportingTagFilterTests</c>.</para>
///
/// <para>This is phase-32b's precedent -- a confirm-live pass can falsify an earlier one -- and the
/// reason the old rule survived two phases is that it was recorded as inherited rather than as
/// observed. Changing it changes what existing tenants' tag-filtered reports return, which is the
/// point: they were returning the wrong set.</para>
///
/// <para>Not a generic IQueryable helper with a captured Func selector (the phase-9 gotcha) -- these
/// are plain EF queries with no delegate capture, kept to the (DocumentType, tag list) -&gt;
/// matching-ids shape rather than generalized further.</para>
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

        var optionCategories = await LoadOptionCategoriesAsync(db, tagOptionIds, organizationId, cancellationToken);
        if (optionCategories.Count == 0)
        {
            // Every selected option id is unknown to this tenant, so nothing can match it. An empty
            // set is the honest answer and the callers already treat it as "no rows" -- returning
            // null here would silently widen the report to everything (phase-42's "a count of zero
            // is a complete answer", in its filtering form).
            return [];
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

        var hits = await query
            .Select(t => new { t.DocumentId, t.TagOptionId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return MatchingAcrossEveryCategory(
            hits.Select(x => (Key: x.DocumentId, x.TagOptionId)), optionCategories);
    }

    /// <summary>
    /// Phase 36 -- the same narrowing for a report whose rows span <b>several</b> document types,
    /// keyed by the (type, id) pair rather than by id alone. The Journal report is the first: its
    /// rows are GL entries, and an entry points back at its document with exactly that pair.
    ///
    /// <para>Same OR-within / AND-across semantics as the single-type overload, through the same
    /// helper, so the two cannot drift.</para>
    /// </summary>
    public static async Task<HashSet<(DocumentType Type, Guid Id)>?> ResolveMatchingDocumentsAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyList<Guid>? tagOptionIds, CancellationToken cancellationToken)
    {
        if (tagOptionIds is not { Count: > 0 })
        {
            return null;
        }

        var optionCategories = await LoadOptionCategoriesAsync(db, tagOptionIds, organizationId, cancellationToken);
        if (optionCategories.Count == 0)
        {
            return [];
        }

        var hits = await db.TransactionReportingTags
            .Where(t => t.OrganizationId == organizationId && tagOptionIds.Contains(t.TagOptionId))
            .Select(t => new { t.DocumentType, t.DocumentId, t.TagOptionId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return MatchingAcrossEveryCategory(
            hits.Select(x => (Key: (x.DocumentType, x.DocumentId), x.TagOptionId)), optionCategories);
    }

    /// <summary>
    /// The category each selected option belongs to. Read for the selection only -- a handful of
    /// ids -- so this is a lookup by primary key, not a scan.
    /// </summary>
    private static async Task<Dictionary<Guid, Guid>> LoadOptionCategoriesAsync(
        IAppDbContext db, IReadOnlyList<Guid> tagOptionIds, Guid? organizationId, CancellationToken cancellationToken)
    {
        var query = db.ReportingTagOptions.Where(o => tagOptionIds.Contains(o.Id));
        if (organizationId is { } organization)
        {
            query = query.Where(o => o.OrganizationId == organization);
        }

        return await query.ToDictionaryAsync(o => o.Id, o => o.CategoryId, cancellationToken);
    }

    /// <summary>
    /// The rule itself, over already-materialised (document, option) hits: a document is kept when
    /// the set of categories it was hit in covers <b>every</b> category the caller selected from.
    ///
    /// <para>Because a hit only exists for a selected option, "hit in category C" already means
    /// "carries one of the options selected in C" -- which is the OR half. Requiring the count of
    /// distinct hit categories to equal the count of selected categories is the AND half.</para>
    ///
    /// <para>In memory rather than in SQL deliberately: the alternative is one <c>GROUP BY ... HAVING
    /// COUNT(DISTINCT ...)</c> per category count, and the hit set here is one row per tagged
    /// document per matched option -- the tagged subset of a period, not the period.</para>
    /// </summary>
    private static HashSet<TKey> MatchingAcrossEveryCategory<TKey>(
        IEnumerable<(TKey Key, Guid TagOptionId)> hits, Dictionary<Guid, Guid> optionCategories)
        where TKey : notnull
    {
        var requiredCategories = optionCategories.Values.Distinct().Count();

        return
        [
            .. hits
                .Where(x => optionCategories.ContainsKey(x.TagOptionId))
                .GroupBy(x => x.Key)
                .Where(g => g.Select(x => optionCategories[x.TagOptionId]).Distinct().Count() == requiredCategories)
                .Select(g => g.Key),
        ];
    }
}
