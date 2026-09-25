using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Sales.Queries.ListDeliveryNotes;

public sealed class ListDeliveryNotesQueryValidator : AbstractValidator<ListDeliveryNotesQuery>
{
    public ListDeliveryNotesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
        this.ValidateDateRange(x => x.FromDate, x => x.ToDate);
        this.ValidateSort(x => x.Sort, ListSort.DocumentOrderings);
    }
}
