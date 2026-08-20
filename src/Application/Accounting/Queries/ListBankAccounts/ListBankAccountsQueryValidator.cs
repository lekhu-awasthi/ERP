using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListBankAccounts;

public sealed class ListBankAccountsQueryValidator : AbstractValidator<ListBankAccountsQuery>
{
    public ListBankAccountsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
