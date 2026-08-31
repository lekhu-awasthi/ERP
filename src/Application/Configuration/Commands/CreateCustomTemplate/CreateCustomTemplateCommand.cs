using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateCustomTemplate;

public sealed record CreateCustomTemplateCommand(Guid OrganizationId, string Name, CustomTemplateType Type, string Body)
    : IRequest<CreateCustomTemplateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomTemplateManage;
}

public sealed record CreateCustomTemplateResult(Guid Id, string Name, CustomTemplateType Type, string Body, bool IsDefault);
