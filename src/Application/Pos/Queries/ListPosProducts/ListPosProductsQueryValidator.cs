using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Pos.Queries.ListPosProducts;

public sealed class ListPosProductsQueryValidator : AbstractValidator<ListPosProductsQuery>
{
    public ListPosProductsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);

        // A scanned code is an identifier, and the columns it is matched against are at most 60
        // (Barcode) and 50 (Code, SKU) characters.
        RuleFor(x => x.Code).MaximumLength(60);
    }
}
