namespace ErpApp.Application.Accounting;

/// <summary>Shared line-input shape for Create/Update JournalVoucher and PreviewGlPosting -- the
/// client always resubmits its whole current multi-line-editable-table state.</summary>
public sealed record JournalVoucherLineInput(Guid AccountId, decimal Debit, decimal Credit);
