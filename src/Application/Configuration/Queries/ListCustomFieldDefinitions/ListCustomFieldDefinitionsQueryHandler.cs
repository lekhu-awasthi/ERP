using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Queries.ListCustomFieldDefinitions;

public sealed class ListCustomFieldDefinitionsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListCustomFieldDefinitionsQuery, IReadOnlyList<CustomFieldDefinitionDto>>
{
    public async Task<IReadOnlyList<CustomFieldDefinitionDto>> Handle(
        ListCustomFieldDefinitionsQuery request, CancellationToken cancellationToken)
    {
        return await db.CustomFieldDefinitions
            .Where(x => x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.Name)
            .Select(x => new CustomFieldDefinitionDto(x.Id, x.Name, x.Type, x.ApplicableDocumentTypes, x.IsActive))
            .ToListAsync(cancellationToken);
    }
}
