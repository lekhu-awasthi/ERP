using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListPayments;

public sealed class ListPaymentsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPaymentsQuery, PagedResult<Payment>>
{
    public async Task<PagedResult<Payment>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Payments.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (request.Direction is { } direction)
        {
            query = query.Where(x => x.Direction == direction);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
