using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.CreateContactGroup;

public sealed record CreateContactGroupCommand(Guid OrganizationId, string Name, Guid? ParentGroupId)
    : IRequest<CreateContactGroupResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactGroupManage;
}

public sealed record CreateContactGroupResult(Guid Id, string Name, Guid? ParentGroupId);
