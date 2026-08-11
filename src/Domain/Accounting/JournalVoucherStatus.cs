namespace ErpApp.Domain.Accounting;

/// <summary>
/// Draft/Approve lifecycle (architecture-spec.md §3.2). Void is modeled as a placeholder member
/// now (mirrors DocumentNumberingRule-adjacent "build the seam, not the feature" precedent) --
/// no VoidJournalVoucherCommand exists yet, the roadmap's Phase 4 exit criteria only names
/// Draft->Approve->GL-post->view.
/// </summary>
public enum JournalVoucherStatus
{
    Draft,
    Approved,
    Void,
}
