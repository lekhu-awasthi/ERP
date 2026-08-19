using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.ApprovePayment;

public sealed record ApprovePaymentCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApprovePaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PaymentApprove;
    public DocumentType LockDateDocumentType => DocumentType.Payment;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApprovePaymentResult(Guid Id, string Code, PaymentStatus Status, DateTimeOffset? ApprovedAt);
