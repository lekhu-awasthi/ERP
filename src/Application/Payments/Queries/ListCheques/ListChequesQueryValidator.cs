using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Payments.Queries.ListCheques;

public sealed class ListChequesQueryValidator : AbstractValidator<ListChequesQuery>
{
    public ListChequesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
