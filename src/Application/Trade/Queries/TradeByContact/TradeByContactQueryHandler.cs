using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Trade.Queries.TradeByContact;

/// <summary>
/// Groups <see cref="TradeLineReader"/>'s facts by contact. A contact whose activity nets to
/// exactly zero across every column is dropped, the same rule the balance and ageing reports use --
/// a row of five zeroes tells a reader nothing and pushes the rows that matter onto page two.
/// </summary>
public sealed class TradeByContactQueryHandler(IAppDbContext db)
    : IRequestHandler<TradeByContactQuery, TradeByContactDto>
{
    public async Task<TradeByContactDto> Handle(TradeByContactQuery request, CancellationToken cancellationToken)
    {
        var facts = await TradeLineReader.LoadAsync(
            db, request.OrganizationId, request.Side, request.FromDate, request.ToDate, cancellationToken);

        var contactIds = facts.Select(x => x.ContactId).Distinct().ToList();

        var contactsQuery = db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id));

        if (request.ContactGroupId is { } groupId)
        {
            contactsQuery = contactsQuery.Where(x => x.GroupId == groupId);
        }

        var contacts = await contactsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId != null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = await db.ContactGroups
            .Where(x => groupIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var rows = facts
            .Where(x => contacts.ContainsKey(x.ContactId))
            .GroupBy(x => x.ContactId)
            .Select(g =>
            {
                var contact = contacts[g.Key];
                return new TradeByContactRowDto(
                    contact.Id,
                    contact.Code,
                    contact.Name,
                    contact.GroupId is { } gid ? groupNames.GetValueOrDefault(gid) : null,
                    g.Sum(x => x.Amount),
                    g.Sum(x => x.Discount),
                    g.Sum(x => x.NetAmount),
                    g.Sum(x => x.VatAmount),
                    g.Sum(x => x.TotalAmount));
            })
            .Where(x => x.Amount != 0 || x.Discount != 0 || x.NetAmount != 0 || x.VatAmount != 0 || x.TotalAmount != 0)
            .OrderBy(x => x.ContactCode, StringComparer.Ordinal)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new TradeByContactDto(
            request.Side,
            request.FromDate,
            request.ToDate,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            rows.Sum(x => x.Amount),
            rows.Sum(x => x.Discount),
            rows.Sum(x => x.NetAmount),
            rows.Sum(x => x.VatAmount),
            rows.Sum(x => x.TotalAmount));
    }
}
