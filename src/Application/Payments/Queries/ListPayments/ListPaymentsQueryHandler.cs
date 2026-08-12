using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.ListPayments;

public sealed class ListPaymentsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPaymentsQuery, IReadOnlyList<Payment>>
{
    public async Task<IReadOnlyList<Payment>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Payments.Where(x => x.OrganizationId == request.OrganizationId && x.Direction == PaymentDirection.Received);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
