using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ProductionSummary;

public sealed class ProductionSummaryQueryValidator : AbstractValidator<ProductionSummaryQuery>
{
    public ProductionSummaryQueryValidator()
    {
        RuleFor(x => x.ToDate).GreaterThanOrEqualTo(x => x.FromDate);
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
