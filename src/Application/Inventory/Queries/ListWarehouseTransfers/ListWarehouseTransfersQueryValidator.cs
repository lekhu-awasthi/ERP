using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Inventory.Queries.ListWarehouseTransfers;

public sealed class ListWarehouseTransfersQueryValidator : AbstractValidator<ListWarehouseTransfersQuery>
{
    public ListWarehouseTransfersQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
        this.ValidateDateRange(x => x.FromDate, x => x.ToDate);
    }
}
