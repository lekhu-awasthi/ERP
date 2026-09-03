using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.JournalReport;

public sealed class JournalReportQueryValidator : AbstractValidator<JournalReportQuery>
{
    public JournalReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("To Date must be on or after From Date.");
    }
}
