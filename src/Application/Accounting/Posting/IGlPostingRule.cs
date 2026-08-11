using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Accounting.Posting;

/// <summary>
/// Pure TDocument -> GL lines mapper (architecture-spec.md §3.4) -- callable both from a
/// PreviewGlPostingQuery (before the user even saves) and the real Approve command handler, so
/// the debit/credit computation never lives in two places. No I/O: whatever a rule needs must
/// already be present on TDocument itself -- see JournalVoucherPostingRule/CashTransferPostingRule,
/// both of which only read data already loaded onto the aggregate.
/// </summary>
public interface IGlPostingRule<in TDocument>
{
    IReadOnlyList<GlLineInput> BuildLines(TDocument document);
}
