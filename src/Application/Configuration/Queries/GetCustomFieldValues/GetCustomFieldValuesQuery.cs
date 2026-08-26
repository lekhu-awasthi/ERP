using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration.Commands.SetCustomFieldValues;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.GetCustomFieldValues;

public sealed record GetCustomFieldValuesQuery(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId)
    : IRequest<IReadOnlyList<CustomFieldValueDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => CustomFieldValuePermissions.ViewPermissionFor(DocumentType);
}

public sealed record CustomFieldValueDto(Guid FieldDefinitionId, string Value);
