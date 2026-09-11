using ErpApp.Domain.Common;

namespace ErpApp.Domain.Accounting;

/// <summary>
/// The posted GL record every ApprovableTransaction's Approve() produces (architecture-spec.md
/// §3.4) -- SourceDocumentType/SourceDocumentId point back at whichever document (JournalVoucher,
/// CashTransfer, and later Invoice/PurchaseBill/Payment) triggered the posting.
///
/// Post() is the single place the balanced-GL invariant (sum(Debit)==sum(Credit)) is enforced for
/// every document type, present and future -- the Journal Voucher's live "Difference: Rs. 0" check
/// generalized (architecture-spec.md §3.4). Callers build the input GlLine list via an
/// IGlPostingRule&lt;TDocument&gt; (Application layer) so the exact same pure function backs both
/// a PreviewGlPostingQuery and the real Approve command handler -- no duplicated debit/credit math.
/// </summary>
public sealed class GlJournalEntry
{
    private readonly List<GlLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public Guid SourceDocumentId { get; private set; }
    public DateTimeOffset PostedAt { get; private set; }

    /// <summary>
    /// Phase 35b -- the <b>billing location</b> this entry was posted from, copied from its source
    /// document at post time.
    ///
    /// <para><b>Why the entry carries one rather than every report joining back.</b> The reference
    /// product filters Trial Balance, Balance Sheet, Income Statement, the Journal report and all
    /// three General Ledger reports by Billing Location (confirm-live 2026-09-10, a census of all 49
    /// report filter bars). Phase 32 sized the location schema for the 15 transactional types plus
    /// the two opening-balance kinds and stopped there, so the alternative was an 11-way join from
    /// GlLine back to whichever document each entry came from -- <c>GlSourceDocumentResolver</c>'s
    /// shape (phase 26a), whose cost is linear in the <i>period</i> rather than the page (phase 34c).
    /// A financial statement aggregates before it pages, so that join would run over every entry in
    /// the window on every request, on nine reports. One nullable column stamped once at post time
    /// turns all of them into a <c>Where</c>.</para>
    ///
    /// <para><b>Null is the honest answer, in three cases that must not be conflated with each
    /// other:</b> an entry posted before this phase whose source document carries no location, an
    /// entry whose document was raised while its type was outside the tenant's
    /// <c>LocationScopeMode</c>, and a tenant that has never had the Billing Location entitlement.
    /// All three mean "no location", and a filter for a specific location excludes them -- which is
    /// what <c>x.LocationId == id</c> does, since a null never equals a value.</para>
    ///
    /// <para><b>A reversal inherits the original's location</b> -- see
    /// <see cref="PostReversalOf"/>. Not a copy for tidiness: a void that landed at a different
    /// location (or at none) would leave the original branch's Trial Balance permanently off by the
    /// document's value while the organization-wide total still balanced -- phase-6 bug #3's failure
    /// mode with the location as the axis instead of an account.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

    public IReadOnlyList<GlLine> Lines => _lines;

    private GlJournalEntry()
    {
    }

    public static GlJournalEntry Post(
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        IReadOnlyList<GlLineInput> lines,
        Guid? locationId = null)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A GL journal entry needs at least one line.");
        }

        if (lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit))
        {
            throw new InvalidOperationException("A GL journal entry's total Debit must equal its total Credit.");
        }

        var entry = new GlJournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId,
            PostedAt = DateTimeOffset.UtcNow,
            LocationId = locationId,
        };

        foreach (var line in lines)
        {
            entry._lines.Add(GlLine.Create(entry.Id, line.AccountId, line.Debit, line.Credit));
        }

        return entry;
    }

    /// <summary>
    /// Void lifecycle (roadmap Phase 16a): posts a second, mirror-image entry against the same
    /// SourceDocumentType/SourceDocumentId -- every Debit/Credit swapped, line for line -- rather
    /// than mutating or deleting <paramref name="original"/>. GlJournalEntry stays append-only
    /// (no UPDATE/DELETE path exists anywhere in this codebase for a posted entry), and mirroring
    /// the original's own already-posted lines exactly (not recomputing from a posting rule) is
    /// foolproof against the Phase 6 bug #3 failure mode -- there is no way for a swap-every-line
    /// mirror to leave any *individual* account it touched unbalanced, unlike a hand-written
    /// reverse posting rule that has to remember every leg (TDS Payable, a separate Inventory
    /// account, etc.) the original touched. Every existing report (Trial Balance/Balance Sheet/
    /// Income Statement) sums GlLines by account with no per-document uniqueness assumption, so a
    /// second entry for the same source document nets to zero by construction, with no report code
    /// changed for this to work. GlJournalEntryConfiguration's own index on
    /// (SourceDocumentType, SourceDocumentId) is non-unique, so a second entry for the same
    /// document id is a schema no-op, not a migration.
    /// </summary>
    public static GlJournalEntry PostReversalOf(GlJournalEntry original)
    {
        var mirroredLines = original.Lines
            .Select(x => new GlLineInput(x.AccountId, x.Credit, x.Debit))
            .ToList();

        return Post(
            original.OrganizationId,
            original.SourceDocumentType,
            original.SourceDocumentId,
            mirroredLines,
            original.LocationId);
    }
}
