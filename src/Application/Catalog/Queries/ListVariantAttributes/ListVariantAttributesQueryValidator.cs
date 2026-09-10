using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Catalog.Queries.ListVariantAttributes;

/// <summary>
/// Phase 34b — this query was the one paginated list in the codebase with no validator at all, so
/// its Page/PageSize were never bounded either. Found by the search sweep rather than looked for:
/// adding one rule to every searchable list is what made the gap visible.
/// </summary>
public sealed class ListVariantAttributesQueryValidator : AbstractValidator<ListVariantAttributesQuery>
{
    public ListVariantAttributesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
    }
}
