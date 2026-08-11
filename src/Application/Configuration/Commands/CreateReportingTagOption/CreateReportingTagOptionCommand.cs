using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateReportingTagOption;

public sealed record CreateReportingTagOptionCommand(Guid OrganizationId, string Name, Guid CategoryId)
    : IRequest<CreateReportingTagOptionResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ReportingTagOptionManage;
}

public sealed record CreateReportingTagOptionResult(Guid Id, string Name, Guid CategoryId);
