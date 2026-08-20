using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.UpdatePayment;

public sealed record UpdatePaymentCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, Guid? PaymentModeId, Guid AccountId, decimal Amount,
    string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations)
    : IRequest<UpdatePaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PaymentEdit;
    public DocumentType AuditDocumentType => DocumentType.Payment;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdatePaymentResult(Guid Id, string Code, PaymentStatus Status);
