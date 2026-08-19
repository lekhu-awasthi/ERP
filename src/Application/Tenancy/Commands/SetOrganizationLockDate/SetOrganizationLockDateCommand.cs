using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.SetOrganizationLockDate;

/// <summary>
/// Sets or clears (LockDate: null) Organization.LockDate (roadmap Phase 16a, NFR-3.4) --
/// Admin-only, the same "Member access here would be self-defeating" bar Phase 14's Tenancy.Role.*
/// keys set: a Member who could move or clear the lock date could reopen exactly the backdated-
/// write window this feature exists to close. Deliberately not itself lock-date-sensitive
/// (ILockDateSensitive/ILockDateSensitiveDocument) -- moving the lock date is an Organization
/// setting, not a ledger-dated document.
/// </summary>
public sealed record SetOrganizationLockDateCommand(Guid OrganizationId, DateOnly? LockDate)
    : IRequest<SetOrganizationLockDateResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationLockDateManage;
}

public sealed record SetOrganizationLockDateResult(Guid OrganizationId, DateOnly? LockDate);
