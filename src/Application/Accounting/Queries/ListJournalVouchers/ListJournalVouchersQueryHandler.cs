using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListJournalVouchers;

public sealed class ListJournalVouchersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListJournalVouchersQuery, PagedResult<JournalVoucher>>
{
    public async Task<PagedResult<JournalVoucher>> Handle(ListJournalVouchersQuery request, CancellationToken cancellationToken)
    {
        var query = db.JournalVouchers.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
