using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.ApprovePayment;

/// <summary>Phase 31 -- OverrideNegativeCashBalanceWarning is the Warn-and-allow acknowledgement for
/// <c>TenantSettings.NegativeCashBalanceAction</c>, the same shape phase 7 gave the stock warning
/// and phase 31 gave the credit-limit one. Trailing and optional, so every existing caller is
/// unchanged.</summary>
public sealed record ApprovePaymentCommand(
    Guid OrganizationId, Guid Id, bool OverrideNegativeCashBalanceWarning = false)
    : IRequest<ApprovePaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PaymentApprove;
    public DocumentType LockDateDocumentType => DocumentType.Payment;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApprovePaymentResult(Guid Id, string Code, PaymentStatus Status, DateTimeOffset? ApprovedAt);
