using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Crm.Commands.DeleteSmsTemplate;

public sealed record DeleteSmsTemplateCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsTemplateManage;
}
