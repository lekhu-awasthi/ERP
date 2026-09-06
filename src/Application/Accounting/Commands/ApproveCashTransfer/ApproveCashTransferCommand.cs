using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.ApproveCashTransfer;

/// <summary>Phase 31 -- OverrideNegativeCashBalanceWarning is the Warn-and-allow acknowledgement for
/// <c>TenantSettings.NegativeCashBalanceAction</c>, the same shape phase 7 gave the stock warning
/// and phase 31 gave the credit-limit one. Trailing and optional, so every existing caller is
/// unchanged.</summary>
public sealed record ApproveCashTransferCommand(
    Guid OrganizationId, Guid Id, bool OverrideNegativeCashBalanceWarning = false)
    : IRequest<ApproveCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.CashTransferApprove;
    public DocumentType LockDateDocumentType => DocumentType.CashTransfer;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveCashTransferResult(Guid Id, string Code, CashTransferStatus Status, DateTimeOffset? ApprovedAt);
