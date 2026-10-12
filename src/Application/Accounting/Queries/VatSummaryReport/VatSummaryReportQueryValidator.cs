using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.VatSummaryReport;

public sealed class VatSummaryReportQueryValidator : AbstractValidator<VatSummaryReportQuery>
{
    public VatSummaryReportQueryValidator()
    {
        // A fiscal year outside the BS table is a 404 from the handler, not a 400 -- the range is a property of the calendar
        // data, as on the four Monthly reports (TradeByContactMonthlyQueryValidator).
        this.RuleFor(x => x.FiscalYear)
            .GreaterThan(0)
            .WithMessage("FiscalYear must be a Bikram Sambat year.");
    }
}
