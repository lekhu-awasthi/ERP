using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Posting;

/// <summary>
/// Reads and reverses <b>every</b> GL entry a source document has posted, rather than the one entry
/// most documents have.
///
/// <para><b>Why this exists (phase 36).</b> <c>GlJournalEntry</c> has always been explicit that
/// <c>(SourceDocumentType, SourceDocumentId)</c> is non-unique -- phase 16a's void posts a second,
/// mirror-image entry against the same pair on purpose. What every call site nonetheless assumed
/// was narrower and unwritten: that <i>while a document is still Approved</i> it has posted exactly
/// one entry, so a `SingleAsync` over the pair is safe. Phase 36 breaks that for two types --
/// allocating further against an Approved Payment or Journal Voucher now posts the realised forex
/// leg as its own entry (see <c>ApplyPaymentAllocationCommandHandler</c>) -- and it was already
/// false for a third: editing an Opening Balance line twice left three entries behind, and the
/// second edit threw <c>InvalidOperationException</c> out of `SingleAsync` as a 500.</para>
///
/// <para><b>The reversal nets rather than mirroring entry by entry</b>, because the Opening Balance
/// case is not a void: its entries are a history of posting, reversing and re-posting, and only
/// their net is outstanding. Netting handles the void case identically (a document voided from
/// Approved has no reversal yet, so the net <i>is</i> the sum of its originals) while being the
/// only form that is also correct when reversals are already present. Entries are grouped by
/// <c>LocationId</c> first, so a document whose location changed between postings has each
/// location's balance reversed where it was posted -- the rule
/// <see cref="GlJournalEntry.PostReversalOf"/> states for a void, applied across entries.</para>
/// </summary>
internal static class SourceDocumentGlEntries
{
    /// <summary>Every entry posted for one source document, lines included.</summary>
    public static async Task<List<GlJournalEntry>> LoadAsync(
        IAppDbContext db, DocumentType sourceDocumentType, Guid sourceDocumentId, CancellationToken cancellationToken)
    {
        return await db.GlJournalEntries
            .Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == sourceDocumentType && x.SourceDocumentId == sourceDocumentId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the entries that reverse whatever <paramref name="sourceDocumentId"/> still has
    /// outstanding in the ledger, and returns them. Adds nothing when the document has posted
    /// nothing, or when everything it posted is already reversed -- so calling it twice is a no-op
    /// the second time rather than a double reversal.
    /// </summary>
    public static async Task<IReadOnlyList<GlJournalEntry>> ReverseOutstandingAsync(
        IAppDbContext db, DocumentType sourceDocumentType, Guid sourceDocumentId, CancellationToken cancellationToken)
    {
        var entries = await LoadAsync(db, sourceDocumentType, sourceDocumentId, cancellationToken);

        var reversals = BuildReversals(entries);

        foreach (var reversal in reversals)
        {
            db.GlJournalEntries.Add(reversal);
        }

        return reversals;
    }

    /// <summary>The pure half, so the netting is testable without a DbContext.</summary>
    public static IReadOnlyList<GlJournalEntry> BuildReversals(IReadOnlyList<GlJournalEntry> entries)
    {
        var reversals = new List<GlJournalEntry>();

        foreach (var group in entries.GroupBy(x => x.LocationId))
        {
            var net = group
                .SelectMany(x => x.Lines)
                .GroupBy(x => x.AccountId)
                .Select(g => new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
                .Where(x => x.Net != 0)
                // A net debit is reversed by a credit of the same size, and the other way round.
                .Select(x => x.Net > 0
                    ? new GlLineInput(x.AccountId, 0m, x.Net)
                    : new GlLineInput(x.AccountId, -x.Net, 0m))
                .ToList();

            if (net.Count == 0)
            {
                continue;
            }

            var first = group.First();
            reversals.Add(GlJournalEntry.Post(
                first.OrganizationId, first.SourceDocumentType, first.SourceDocumentId, net, group.Key));
        }

        return reversals;
    }
}
