using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.PurchaseMasterReport;

public sealed class PurchaseMasterReportQueryValidator : AbstractValidator<PurchaseMasterReportQuery>
{
    public PurchaseMasterReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
