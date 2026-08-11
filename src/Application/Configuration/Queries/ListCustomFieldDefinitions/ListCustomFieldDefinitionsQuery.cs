using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.ListCustomFieldDefinitions;

public sealed record ListCustomFieldDefinitionsQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<CustomFieldDefinitionDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomFieldDefinitionView;
}

public sealed record CustomFieldDefinitionDto(
    Guid Id, string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes, bool IsActive);
