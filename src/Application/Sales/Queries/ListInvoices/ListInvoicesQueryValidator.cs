using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.ListInvoices;

public sealed class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    public ListInvoicesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
