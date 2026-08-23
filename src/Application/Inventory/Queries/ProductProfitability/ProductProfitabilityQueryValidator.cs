using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.ProductProfitability;

public sealed class ProductProfitabilityQueryValidator : AbstractValidator<ProductProfitabilityQuery>
{
    public ProductProfitabilityQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
