using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Crm.Queries.ListDeals;

public sealed class ListDealsQueryValidator : AbstractValidator<ListDealsQuery>
{
    public ListDealsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
    }
}
