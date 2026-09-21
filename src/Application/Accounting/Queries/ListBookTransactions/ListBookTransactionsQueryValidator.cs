using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListBookTransactions;

public sealed class ListBookTransactionsQueryValidator : AbstractValidator<ListBookTransactionsQuery>
{
    public ListBookTransactionsQueryValidator()
    {
        RuleFor(x => x.BankAccountId).NotEmpty();

        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // An inverted range returns nothing on every list it reaches and reads as data loss, which
        // is why the guard insists on this half even where there is no search term (phase 34b).
        this.ValidateDateRange(x => x.FromDate, x => x.ToDate);
    }
}
