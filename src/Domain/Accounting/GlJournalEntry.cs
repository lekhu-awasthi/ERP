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
}
