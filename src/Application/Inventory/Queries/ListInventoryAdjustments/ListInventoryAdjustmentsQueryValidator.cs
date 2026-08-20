using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;

public sealed class ListInventoryAdjustmentsQueryValidator : AbstractValidator<ListInventoryAdjustmentsQuery>
{
    public ListInventoryAdjustmentsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
