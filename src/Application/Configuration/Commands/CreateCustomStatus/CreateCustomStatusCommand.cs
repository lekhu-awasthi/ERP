using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateCustomStatus;

public sealed record CreateCustomStatusCommand(Guid OrganizationId, string Name, DocumentType DocumentType)
    : IRequest<CreateCustomStatusResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomStatusManage;
}

public sealed record CreateCustomStatusResult(Guid Id, string Name, DocumentType DocumentType);
