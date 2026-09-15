using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Queries.ListSmsLogs;

public sealed class ListSmsLogsQueryHandler(IAppDbContext db) : IRequestHandler<ListSmsLogsQuery, SmsLogListDto>
{
    public async Task<SmsLogListDto> Handle(ListSmsLogsQuery request, CancellationToken cancellationToken)
    {
        var query = db.SmsLogs.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.ContactId == contactId);
        }

        // Inline Contains over this row's own columns -- see SearchTerm's remarks for why there is
        // no shared matcher (a static call and the StringComparison overload are both untranslatable,
        // and InMemory hides it by evaluating them in C#).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x =>
                x.Title.Contains(term) || x.Content.Contains(term) || x.PhoneNumber.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.SentAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.BatchId, x.ContactId, x.Title, x.Content, x.PhoneNumber, x.CreditsUsed, x.SentAt,
            })
            .ToListAsync(cancellationToken);

        var contactIds = rows.Select(x => x.ContactId).Distinct().ToList();
        var contactNames = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var dtoRows = rows.Select(x => new SmsLogRowDto(
            x.Id, x.BatchId, x.ContactId, contactNames.GetValueOrDefault(x.ContactId, "—"), x.Title, x.Content,
            x.PhoneNumber, x.CreditsUsed, x.SentAt))
            .ToList();

        return new SmsLogListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
