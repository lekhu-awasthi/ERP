using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.DeactivateContact;

/// <summary>Soft-delete (IsActive=false) -- Contact gets referenced by transactional documents
/// from Phase 5+, so hard delete isn't safe once that's true. Implements IRequest&lt;Unit&gt;
/// explicitly, same reasoning as DeleteLookupCommand&lt;TLookup&gt;.</summary>
public sealed record DeactivateContactCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactManage;
}
