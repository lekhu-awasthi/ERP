using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateReportingTagOption;

public sealed record UpdateReportingTagOptionCommand(Guid OrganizationId, Guid Id, string Name, Guid CategoryId, bool IsActive)
    : IRequest<UpdateReportingTagOptionResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ReportingTagOptionManage;
}

public sealed record UpdateReportingTagOptionResult(Guid Id, string Name, Guid CategoryId, bool IsActive);
