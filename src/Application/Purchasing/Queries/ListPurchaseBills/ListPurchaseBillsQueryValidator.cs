using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseBills;

public sealed class ListPurchaseBillsQueryValidator : AbstractValidator<ListPurchaseBillsQuery>
{
    public ListPurchaseBillsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
