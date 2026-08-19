using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.ListExpenses;

public sealed class ListExpensesQueryValidator : AbstractValidator<ListExpensesQuery>
{
    public ListExpensesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
