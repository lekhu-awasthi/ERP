using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.DeleteCustomFieldDefinition;

public sealed record DeleteCustomFieldDefinitionCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomFieldDefinitionManage;
}
