using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.ListExpenses;

public sealed class ListExpensesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListExpensesQuery, IReadOnlyList<Expense>>
{
    public async Task<IReadOnlyList<Expense>> Handle(ListExpensesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Expenses.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
