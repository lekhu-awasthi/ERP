using ErpApp.Application.Common.Security;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.GetTransactionReportingTags;

public sealed record GetTransactionReportingTagsQuery(Guid OrganizationId, DocumentType DocumentType, Guid DocumentId)
    : IRequest<IReadOnlyList<TransactionReportingTagDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => TransactionReportingTagPermissions.ViewPermissionFor(DocumentType);
}

public sealed record TransactionReportingTagDto(Guid TagOptionId, string TagOptionName, Guid CategoryId, string CategoryName);
