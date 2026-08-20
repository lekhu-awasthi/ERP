using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdatePaymentMode;

public sealed record UpdatePaymentModeCommand(Guid OrganizationId, Guid Id, string Name, bool IsActive, bool RequiresChequeDetails)
    : IRequest<UpdatePaymentModeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentModeManage;
}

public sealed record UpdatePaymentModeResult(Guid Id, string Name, bool IsActive, bool RequiresChequeDetails);
