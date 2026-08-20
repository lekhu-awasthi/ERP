using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListAccounts;

public sealed class ListAccountsQueryValidator : AbstractValidator<ListAccountsQuery>
{
    public ListAccountsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
