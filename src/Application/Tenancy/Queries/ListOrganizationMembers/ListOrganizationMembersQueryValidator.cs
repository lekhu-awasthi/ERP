using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Tenancy.Queries.ListOrganizationMembers;

public sealed class ListOrganizationMembersQueryValidator : AbstractValidator<ListOrganizationMembersQuery>
{
    public ListOrganizationMembersQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
