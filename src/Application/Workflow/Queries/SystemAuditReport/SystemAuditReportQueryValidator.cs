using ErpApp.Application.Common.Pagination;
using FluentValidation;

namespace ErpApp.Application.Workflow.Queries.SystemAuditReport;

public sealed class SystemAuditReportQueryValidator : AbstractValidator<SystemAuditReportQuery>
{
    public SystemAuditReportQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
