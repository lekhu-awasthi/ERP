using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomFieldDefinition;

public sealed record UpdateCustomFieldDefinitionCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    CustomFieldType Type,
    IReadOnlyList<DocumentType> ApplicableDocumentTypes,
    bool IsActive)
    : IRequest<UpdateCustomFieldDefinitionResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomFieldDefinitionManage;
}

public sealed record UpdateCustomFieldDefinitionResult(
    Guid Id, string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes, bool IsActive);
