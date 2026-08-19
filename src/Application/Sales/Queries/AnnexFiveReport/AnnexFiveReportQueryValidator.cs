using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.AnnexFiveReport;

public sealed class AnnexFiveReportQueryValidator : AbstractValidator<AnnexFiveReportQuery>
{
    public AnnexFiveReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
