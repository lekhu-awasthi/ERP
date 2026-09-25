using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.InventoryVarianceReport;

public sealed class InventoryVarianceReportQueryValidator : AbstractValidator<InventoryVarianceReportQuery>
{
    public InventoryVarianceReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        RuleFor(x => x.AsOfDate).NotEmpty();
    }
}
