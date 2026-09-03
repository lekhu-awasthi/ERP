using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Workflow.Queries.TransactionList;

public sealed class TransactionListQueryValidator : AbstractValidator<TransactionListQuery>
{
    public TransactionListQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // Both dates are optional (an unfiltered list is the report's own default), so the range
        // rule only applies when both are supplied.
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .When(x => x.FromDate is not null && x.ToDate is not null)
            .WithMessage("To Date must be on or after From Date.");
    }
}
