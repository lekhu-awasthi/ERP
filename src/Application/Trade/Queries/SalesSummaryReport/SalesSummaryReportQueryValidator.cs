using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Trade.Queries.SalesSummaryReport;

public sealed class SalesSummaryReportQueryValidator : AbstractValidator<SalesSummaryReportQuery>
{
    public SalesSummaryReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // A fiscal year outside the BS table is a 404 from the handler, not a 422 -- the range is a
        // property of the calendar data, and NotFoundException is what the handler already throws.
        this.RuleFor(x => x.FiscalYear)
            .GreaterThan(0)
            .WithMessage("FiscalYear must be a Bikram Sambat year.");
    }
}
