using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ListBillsOfMaterials;

public sealed class ListBillsOfMaterialsQueryValidator : AbstractValidator<ListBillsOfMaterialsQuery>
{
    public ListBillsOfMaterialsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
