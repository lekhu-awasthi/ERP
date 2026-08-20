using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.ListOpeningStockLines;

public sealed class ListProductOpeningBalancesQueryValidator : AbstractValidator<ListProductOpeningBalancesQuery>
{
    public ListProductOpeningBalancesQueryValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
