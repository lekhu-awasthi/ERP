using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.VoidPayment;

public sealed record VoidPaymentCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidPaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PaymentVoid;
    public DocumentType LockDateDocumentType => DocumentType.Payment;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidPaymentResult(Guid Id, string Code, PaymentStatus Status, DateTimeOffset? VoidedAt);
