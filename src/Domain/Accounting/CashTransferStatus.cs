namespace ErpApp.Domain.Accounting;

/// <summary>Same Draft/Approve/Void semantics as JournalVoucherStatus, own enum for type clarity
/// (this codebase doesn't share a base class/enum across aggregates -- see JournalVoucherStatus's
/// doc comment).</summary>
public enum CashTransferStatus
{
    Draft,
    Approved,
    Void,
}
