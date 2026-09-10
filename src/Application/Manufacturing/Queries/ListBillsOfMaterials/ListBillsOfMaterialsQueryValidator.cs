using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ListBillsOfMaterials;

public sealed class ListBillsOfMaterialsQueryValidator : AbstractValidator<ListBillsOfMaterialsQuery>
{
    public ListBillsOfMaterialsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);

        // Phase 34b -- this query has taken a Search term since phase 25 and never bounded its
        // length. The sweep guard is what surfaced that: it was the one searchable list in the
        // codebase whose term reached a LIKE with no cap on it.
        this.ValidateSearch(x => x.Search);
    }
}
