using ErpApp.Application.Common.Security;
using ErpApp.Application.Crm.Commands.CreateSmsTemplate;
using MediatR;

namespace ErpApp.Application.Crm.Commands.UpdateSmsTemplate;

public sealed record UpdateSmsTemplateCommand(Guid OrganizationId, Guid Id, string Title, string Content)
    : IRequest<SmsTemplateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsTemplateManage;
}
