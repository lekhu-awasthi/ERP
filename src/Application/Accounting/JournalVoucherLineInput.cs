namespace ErpApp.Application.Accounting;

/// <summary>Shared line-input shape for Create/Update JournalVoucher and PreviewGlPosting -- the
/// client always resubmits its whole current multi-line-editable-table state. ContactId (decision
/// #2, docs/phase-17-status.md) optionally tags a line as posting against a Contact's own AR/AP
/// control account, making it an allocatable credit source once Approved.</summary>
public sealed record JournalVoucherLineInput(Guid AccountId, decimal Debit, decimal Credit, Guid? ContactId = null);
