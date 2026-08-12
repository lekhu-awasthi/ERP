using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListExpenses;

public sealed record ListExpensesQuery(Guid OrganizationId, ExpenseStatus? Status) : IRequest<IReadOnlyList<Expense>>;
