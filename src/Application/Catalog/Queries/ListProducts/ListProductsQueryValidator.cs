using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
