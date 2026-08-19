using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.ListQuotations;

public sealed class ListQuotationsQueryValidator : AbstractValidator<ListQuotationsQuery>
{
    public ListQuotationsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
