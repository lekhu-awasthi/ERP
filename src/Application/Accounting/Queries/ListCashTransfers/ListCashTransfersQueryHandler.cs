using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListCashTransfers;

public sealed class ListCashTransfersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListCashTransfersQuery, PagedResult<CashTransfer>>
{
    public async Task<PagedResult<CashTransfer>> Handle(ListCashTransfersQuery request, CancellationToken cancellationToken)
    {
        var query = db.CashTransfers.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
