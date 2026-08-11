using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomStatus;

public sealed record UpdateCustomStatusCommand(Guid OrganizationId, Guid Id, string Name, DocumentType DocumentType, bool IsActive)
    : IRequest<UpdateCustomStatusResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CustomStatusManage;
}

public sealed record UpdateCustomStatusResult(Guid Id, string Name, DocumentType DocumentType, bool IsActive);
