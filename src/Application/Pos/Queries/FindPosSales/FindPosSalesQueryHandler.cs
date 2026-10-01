using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.FindPosSales;

public sealed class FindPosSalesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<FindPosSalesQuery, IReadOnlyList<PosSaleMatchDto>>
{
    public async Task<IReadOnlyList<PosSaleMatchDto>> Handle(FindPosSalesQuery request, CancellationToken cancellationToken)
    {
        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceView, request.LocationId,
            cancellationToken);

        var query = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId
            && x.LocationId == request.LocationId && x.Channel == SalesChannel.Pos
            && x.Status == InvoiceStatus.Approved);

        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // String.Contains, which SQL Server turns into LIKE and InMemory can evaluate (the
            // EF.Functions.Like gotcha); case-insensitive on SQL Server by collation.
            query = query.Where(x => x.Code.Contains(term));
        }

        var rows = await query
            .OrderByDescending(x => x.ApprovedAt)
            .Take(FindPosSalesQuery.MaxResults)
            .Select(x => new { x.Id, x.Code, x.Date, x.ApprovedAt, x.PosSessionId, x.ContactId, x.RoundOff })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(x => x.Id).ToList();
        var totals = await db.InvoiceLines
            .Where(x => ids.Contains(x.InvoiceId))
            .GroupBy(x => x.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, cancellationToken);

        var sessionIds = rows.Select(x => x.PosSessionId).OfType<Guid>().Distinct().ToList();
        var sessionCodes = await db.PosSessions
            .Where(x => x.OrganizationId == request.OrganizationId && sessionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var contactIds = rows.Select(x => x.ContactId).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsWalkInCustomer }, cancellationToken);

        return rows
            .Select(x =>
            {
                var contact = contacts.GetValueOrDefault(x.ContactId);
                return new PosSaleMatchDto(
                    x.Id,
                    x.Code,
                    x.Date,
                    x.ApprovedAt,
                    x.PosSessionId is { } s ? sessionCodes.GetValueOrDefault(s) : null,
                    contact?.Name ?? "",
                    contact?.IsWalkInCustomer ?? false,
                    totals.GetValueOrDefault(x.Id) + x.RoundOff);
            })
            .ToList();
    }
}
