using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.StockAgeing;

public sealed class StockAgeingQueryValidator : AbstractValidator<StockAgeingQuery>
{
    public StockAgeingQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
