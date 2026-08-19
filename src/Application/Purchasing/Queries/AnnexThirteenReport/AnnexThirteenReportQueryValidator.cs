using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;

public sealed class AnnexThirteenReportQueryValidator : AbstractValidator<AnnexThirteenReportQuery>
{
    public AnnexThirteenReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
