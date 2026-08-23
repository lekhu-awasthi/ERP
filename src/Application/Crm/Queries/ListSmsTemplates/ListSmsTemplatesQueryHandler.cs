using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Queries.ListSmsTemplates;

public sealed class ListSmsTemplatesQueryHandler(IAppDbContext db) : IRequestHandler<ListSmsTemplatesQuery, SmsTemplateListDto>
{
    public async Task<SmsTemplateListDto> Handle(ListSmsTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = db.SmsTemplates.Where(x => x.OrganizationId == request.OrganizationId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new SmsTemplateRowDto(x.Id, x.Title, x.Content, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return new SmsTemplateListDto(rows, request.Page, request.PageSize, totalCount);
    }
}
