using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Workflow.Queries.ListTasks;

public sealed class ListTasksQueryValidator : AbstractValidator<ListTasksQuery>
{
    public ListTasksQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
