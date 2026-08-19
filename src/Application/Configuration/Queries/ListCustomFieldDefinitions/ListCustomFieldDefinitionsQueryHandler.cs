using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Queries.ListCustomFieldDefinitions;

public sealed class ListCustomFieldDefinitionsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListCustomFieldDefinitionsQuery, PagedResult<CustomFieldDefinitionDto>>
{
    public async Task<PagedResult<CustomFieldDefinitionDto>> Handle(
        ListCustomFieldDefinitionsQuery request, CancellationToken cancellationToken)
    {
        return await db.CustomFieldDefinitions
            .Where(x => x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.Name)
            .Select(x => new CustomFieldDefinitionDto(x.Id, x.Name, x.Type, x.ApplicableDocumentTypes, x.IsActive))
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
