using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.GeneralLedgerSummary;

public sealed class GeneralLedgerSummaryQueryValidator : AbstractValidator<GeneralLedgerSummaryQuery>
{
    public GeneralLedgerSummaryQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("To Date must be on or after From Date.");
    }
}
