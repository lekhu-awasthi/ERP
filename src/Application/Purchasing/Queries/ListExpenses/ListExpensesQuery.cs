using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListExpenses;

public sealed record ListExpensesQuery(
    Guid OrganizationId,
    ExpenseStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<Expense>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExpenseView;
}
