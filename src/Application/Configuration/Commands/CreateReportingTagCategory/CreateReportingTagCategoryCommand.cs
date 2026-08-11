using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;

public sealed record CreateReportingTagCategoryCommand(Guid OrganizationId, string Name)
    : IRequest<CreateReportingTagCategoryResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ReportingTagCategoryManage;
}

public sealed record CreateReportingTagCategoryResult(Guid Id, string Name);
