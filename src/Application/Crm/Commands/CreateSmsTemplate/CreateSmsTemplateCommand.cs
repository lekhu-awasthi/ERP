using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Crm.Commands.CreateSmsTemplate;

public sealed record CreateSmsTemplateCommand(Guid OrganizationId, string Title, string Content)
    : IRequest<SmsTemplateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsTemplateManage;
}

public sealed record SmsTemplateResult(Guid Id, string Title, string Content);
