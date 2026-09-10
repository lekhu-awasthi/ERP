using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.ListSalesOrders;

public sealed class ListSalesOrdersQueryValidator : AbstractValidator<ListSalesOrdersQuery>
{
    public ListSalesOrdersQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
        this.ValidateDateRange(x => x.FromDate, x => x.ToDate);
    }
}
