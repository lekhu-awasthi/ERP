using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.ListCustomFieldDefinitions;

public sealed record ListCustomFieldDefinitionsQuery(
    Guid OrganizationId,
    int Page = 1,
    int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<PagedResult<CustomFieldDefinitionDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomFieldDefinitionView;
}

public sealed record CustomFieldDefinitionDto(
    Guid Id, string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes, bool IsActive);
