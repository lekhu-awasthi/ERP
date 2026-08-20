using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Payments.Queries.ListPayments;

public sealed class ListPaymentsQueryValidator : AbstractValidator<ListPaymentsQuery>
{
    public ListPaymentsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
