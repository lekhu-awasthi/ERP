using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Configuration.Queries.ListAlertSendLogs;

public sealed class ListAlertSendLogsQueryValidator : AbstractValidator<ListAlertSendLogsQuery>
{
    public ListAlertSendLogsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
