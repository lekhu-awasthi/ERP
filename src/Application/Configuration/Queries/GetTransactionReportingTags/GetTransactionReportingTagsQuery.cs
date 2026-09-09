using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.GetTransactionReportingTags;

public sealed record GetTransactionReportingTagsQuery(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId)
    : IRequest<IReadOnlyList<TransactionReportingTagDto>>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => TransactionReportingTagPermissions.ViewPermissionFor(DocumentType);

    /// <summary>Phase 32b -- when the parent is a document, the key above is that document&#39;s own, so
    /// a location-scoped caller must hold it at the parent&#39;s location. When the parent is a Contact the
    /// key is not location-scopable and the check never runs.</summary>
    public Guid LocationDocumentId => DocumentId;
}

public sealed record TransactionReportingTagDto(Guid TagOptionId, string TagOptionName, Guid CategoryId, string CategoryName);
