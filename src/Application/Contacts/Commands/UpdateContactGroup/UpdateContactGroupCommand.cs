using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.UpdateContactGroup;

public sealed record UpdateContactGroupCommand(Guid OrganizationId, Guid Id, string Name, Guid? ParentGroupId, bool IsActive)
    : IRequest<UpdateContactGroupResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactGroupManage;
}

public sealed record UpdateContactGroupResult(Guid Id, string Name, Guid? ParentGroupId, bool IsActive);
