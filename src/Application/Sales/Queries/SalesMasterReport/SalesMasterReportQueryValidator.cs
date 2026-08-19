using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.SalesMasterReport;

public sealed class SalesMasterReportQueryValidator : AbstractValidator<SalesMasterReportQuery>
{
    public SalesMasterReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
