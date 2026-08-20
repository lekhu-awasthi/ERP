using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Payments.Queries.ListAllocatablePayments;

public sealed class ListAllocatablePaymentsQueryValidator : AbstractValidator<ListAllocatablePaymentsQuery>
{
    public ListAllocatablePaymentsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
