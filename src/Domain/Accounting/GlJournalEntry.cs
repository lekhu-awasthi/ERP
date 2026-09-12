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
    /// a reversal. Not a copy for tidiness: a void that landed at a different
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

    // Phase 37 removed PostReversalOf. It mirrored one entry's lines, Debit for Credit, and was
    // the right shape while every approved document had exactly one entry -- which phase 36
    // showed was a habit rather than an invariant, and phase 37 made routinely false (a receipt
    // that covers a shortfall posts its cost catch-up against the same source document). Its
    // replacement is Application.Accounting.Posting.SourceDocumentGlEntries.ReverseOutstandingAsync,
    // which nets every entry a document has posted and reverses the net: identical output for the
    // single-entry case it used to serve, and the only form that is also right for the others. It
    // is deleted rather than left for a future caller to reach for, because the one-entry
    // assumption is exactly what kept coming back (phase 33: a pattern is not replaced until its
    // copies are gone).
}
