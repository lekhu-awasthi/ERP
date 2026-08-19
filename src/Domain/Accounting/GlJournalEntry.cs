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

    public IReadOnlyList<GlLine> Lines => _lines;

    private GlJournalEntry()
    {
    }

    public static GlJournalEntry Post(
        Guid organizationId, DocumentType sourceDocumentType, Guid sourceDocumentId, IReadOnlyList<GlLineInput> lines)
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

        return Post(original.OrganizationId, original.SourceDocumentType, original.SourceDocumentId, mirroredLines);
    }
}
