using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ListContactPersonnel;

public sealed class ListContactPersonnelQueryHandler(IAppDbContext db)
    : IRequestHandler<ListContactPersonnelQuery, ContactPersonnelListDto>
{
    public async Task<ContactPersonnelListDto> Handle(ListContactPersonnelQuery request, CancellationToken cancellationToken)
    {
        var query = db.ContactPersonnel.Where(
            x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.Name, x.Address, x.Code, x.Phone, x.GroupId, x.Email, x.OrganizationTitle, x.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var groupIds = rows.Where(x => x.GroupId is not null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var dtoRows = rows.Select(x => new ContactPersonnelRowDto(
            x.Id, x.Name, x.Address, x.Code, x.Phone, x.GroupId,
            x.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
            x.Email, x.OrganizationTitle, x.CreatedAt))
            .ToList();

        return new ContactPersonnelListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
