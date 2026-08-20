using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.ListOpeningBalanceLines;

public sealed class ListAccountOpeningBalancesQueryValidator : AbstractValidator<ListAccountOpeningBalancesQuery>
{
    public ListAccountOpeningBalancesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
