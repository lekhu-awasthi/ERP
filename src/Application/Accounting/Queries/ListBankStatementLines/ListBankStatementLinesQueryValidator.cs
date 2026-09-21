using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListBankStatementLines;

public sealed class ListBankStatementLinesQueryValidator : AbstractValidator<ListBankStatementLinesQuery>
{
    public ListBankStatementLinesQueryValidator()
    {
        RuleFor(x => x.BankAccountId).NotEmpty();

        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
        this.ValidateDateRange(x => x.FromDate, x => x.ToDate);
        this.ValidateSort(x => x.Sort, ListSort.DocumentOrderings);
    }
}
