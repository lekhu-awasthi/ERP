using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.TdsReport;

public sealed class TdsReportQueryValidator : AbstractValidator<TdsReportQuery>
{
    public TdsReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
