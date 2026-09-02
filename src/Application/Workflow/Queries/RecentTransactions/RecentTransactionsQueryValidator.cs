using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Workflow.Queries.RecentTransactions;

public sealed class RecentTransactionsQueryValidator : AbstractValidator<RecentTransactionsQuery>
{
    public RecentTransactionsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // An inverted range would silently return nothing, which reads on a dashboard as "you have
        // no transactions" rather than "you asked for an impossible window".
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("'To Date' must be on or after 'From Date'.");
    }
}
