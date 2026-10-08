using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.ListCreditableInvoices;

public sealed class ListCreditableInvoicesQueryValidator : AbstractValidator<ListCreditableInvoicesQuery>
{
    public ListCreditableInvoicesQueryValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
    }
}
