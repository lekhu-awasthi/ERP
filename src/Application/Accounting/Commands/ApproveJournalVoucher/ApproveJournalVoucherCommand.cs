using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;

/// <summary>Phase 31 -- OverrideNegativeCashBalanceWarning is the Warn-and-allow acknowledgement for
/// <c>TenantSettings.NegativeCashBalanceAction</c>, the same shape phase 7 gave the stock warning
/// and phase 31 gave the credit-limit one. Trailing and optional, so every existing caller is
/// unchanged.</summary>
public sealed record ApproveJournalVoucherCommand(
    Guid OrganizationId, Guid Id, bool OverrideNegativeCashBalanceWarning = false)
    : IRequest<ApproveJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.JournalVoucherApprove;
    public DocumentType LockDateDocumentType => DocumentType.JournalVoucher;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status, DateTimeOffset? ApprovedAt);
