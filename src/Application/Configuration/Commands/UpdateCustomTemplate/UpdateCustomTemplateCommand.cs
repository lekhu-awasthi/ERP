using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomTemplate;

public sealed record UpdateCustomTemplateCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    CustomTemplateType Type,
    string Body,
    bool IsActive)
    : IRequest<UpdateCustomTemplateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomTemplateManage;
}

public sealed record UpdateCustomTemplateResult(
    Guid Id, string Name, CustomTemplateType Type, string Body, bool IsDefault, bool IsActive);
