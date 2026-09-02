using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionOrders;

public sealed class ListProductionOrdersQueryValidator : AbstractValidator<ListProductionOrdersQuery>
{
    public ListProductionOrdersQueryValidator() => this.ValidatePaging(x => x.Page, x => x.PageSize);
}
