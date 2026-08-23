using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Queries.ListSmsCreditLedger;

public sealed class ListSmsCreditLedgerQueryHandler(IAppDbContext db) : IRequestHandler<ListSmsCreditLedgerQuery, SmsCreditLedgerDto>
{
    public async Task<SmsCreditLedgerDto> Handle(ListSmsCreditLedgerQuery request, CancellationToken cancellationToken)
    {
        var query = db.SmsCreditLedgerEntries.Where(x => x.OrganizationId == request.OrganizationId);

        var totalCount = await query.CountAsync(cancellationToken);
        var balance = await query.SumAsync(x => x.ChangeAmount, cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new { x.Id, x.Type, x.ChangeAmount, x.Reason, x.CreatedByUserId, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(x => x.CreatedByUserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var dtoRows = rows.Select(x => new SmsCreditLedgerRowDto(
            x.Id, x.Type, x.ChangeAmount, x.Reason, x.CreatedByUserId, userNames.GetValueOrDefault(x.CreatedByUserId, "—"), x.CreatedAt))
            .ToList();

        return new SmsCreditLedgerDto(balance, dtoRows, request.Page, request.PageSize, totalCount);
    }
}
