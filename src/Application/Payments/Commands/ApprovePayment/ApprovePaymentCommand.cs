using ErpApp.Application.Common.Security;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.ApprovePayment;

public sealed record ApprovePaymentCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApprovePaymentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentApprove;
}

public sealed record ApprovePaymentResult(Guid Id, string Code, PaymentStatus Status, DateTimeOffset? ApprovedAt);
