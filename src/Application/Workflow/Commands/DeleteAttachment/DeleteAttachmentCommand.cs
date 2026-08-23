using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.DeleteAttachment;

public sealed record DeleteAttachmentCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactManage;
}
