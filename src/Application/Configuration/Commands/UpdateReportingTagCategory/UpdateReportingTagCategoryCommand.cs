using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateReportingTagCategory;

public sealed record UpdateReportingTagCategoryCommand(Guid OrganizationId, Guid Id, string Name, bool IsActive)
    : IRequest<UpdateReportingTagCategoryResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ReportingTagCategoryManage;
}

public sealed record UpdateReportingTagCategoryResult(Guid Id, string Name, bool IsActive);
