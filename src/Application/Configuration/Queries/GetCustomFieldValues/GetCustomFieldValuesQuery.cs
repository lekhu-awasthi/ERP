using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration.Commands.SetCustomFieldValues;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.GetCustomFieldValues;

public sealed record GetCustomFieldValuesQuery(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId)
    : IRequest<IReadOnlyList<CustomFieldValueDto>>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => CustomFieldValuePermissions.ViewPermissionFor(DocumentType);

    /// <summary>Phase 32b -- when the parent is a document, the key above is that document&#39;s own, so
    /// a location-scoped caller must hold it at the parent&#39;s location. When the parent is a Contact the
    /// key is not location-scopable and the check never runs.</summary>
    public Guid LocationDocumentId => DocumentId;
}

public sealed record CustomFieldValueDto(Guid FieldDefinitionId, string Value);
