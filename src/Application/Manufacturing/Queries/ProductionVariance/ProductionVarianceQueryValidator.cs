using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ProductionVariance;

public sealed class ProductionVarianceQueryValidator : AbstractValidator<ProductionVarianceQuery>
{
    public ProductionVarianceQueryValidator()
    {
        RuleFor(x => x.ToDate).GreaterThanOrEqualTo(x => x.FromDate);
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
