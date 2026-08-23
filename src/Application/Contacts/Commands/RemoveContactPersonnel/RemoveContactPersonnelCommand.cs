using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.RemoveContactPersonnel;

/// <summary>Not audited (no "Remove"/"Delete" prefix recognized by AuditBehavior) -- consistent
/// with every other delete-shaped command in this codebase (DeactivateContact, DeleteLookup)
/// staying outside the System Audit trail.</summary>
public sealed record RemoveContactPersonnelCommand(Guid OrganizationId, Guid ContactId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactManage;
}
