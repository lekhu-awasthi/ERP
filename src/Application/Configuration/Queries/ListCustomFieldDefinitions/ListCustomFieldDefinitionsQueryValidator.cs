using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Configuration.Queries.ListCustomFieldDefinitions;

public sealed class ListCustomFieldDefinitionsQueryValidator : AbstractValidator<ListCustomFieldDefinitionsQuery>
{
    public ListCustomFieldDefinitionsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
