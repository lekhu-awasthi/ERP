using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;

public sealed class ListPurchaseOrdersQueryValidator : AbstractValidator<ListPurchaseOrdersQuery>
{
    public ListPurchaseOrdersQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
