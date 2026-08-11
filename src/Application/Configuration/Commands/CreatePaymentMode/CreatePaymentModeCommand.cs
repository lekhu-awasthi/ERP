using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreatePaymentMode;

public sealed record CreatePaymentModeCommand(Guid OrganizationId, string Name)
    : IRequest<CreatePaymentModeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentModeManage;
}

public sealed record CreatePaymentModeResult(Guid Id, string Name);
