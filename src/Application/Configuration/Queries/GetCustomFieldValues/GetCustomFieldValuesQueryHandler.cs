using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Queries.GetCustomFieldValues;

public sealed class GetCustomFieldValuesQueryHandler(IAppDbContext db)
    : IRequestHandler<GetCustomFieldValuesQuery, IReadOnlyList<CustomFieldValueDto>>
{
    public async Task<IReadOnlyList<CustomFieldValueDto>> Handle(
        GetCustomFieldValuesQuery request, CancellationToken cancellationToken)
    {
        return await db.CustomFieldValues
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.DocumentType == request.DocumentType && x.DocumentId == request.DocumentId)
            .Select(x => new CustomFieldValueDto(x.FieldDefinitionId, x.Value))
            .ToListAsync(cancellationToken);
    }
}
