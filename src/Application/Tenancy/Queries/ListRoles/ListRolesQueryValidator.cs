using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Tenancy.Queries.ListRoles;

public sealed class ListRolesQueryValidator : AbstractValidator<ListRolesQuery>
{
    public ListRolesQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
