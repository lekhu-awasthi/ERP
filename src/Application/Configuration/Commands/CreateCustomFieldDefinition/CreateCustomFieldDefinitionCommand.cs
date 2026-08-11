using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;

public sealed record CreateCustomFieldDefinitionCommand(
    Guid OrganizationId, string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes)
    : IRequest<CreateCustomFieldDefinitionResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomFieldDefinitionManage;
}

public sealed record CreateCustomFieldDefinitionResult(
    Guid Id, string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes);
