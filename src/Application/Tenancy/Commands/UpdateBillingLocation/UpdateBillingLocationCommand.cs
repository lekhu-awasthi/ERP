using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateBillingLocation;

/// <summary>
/// Edits one billing location, or activates/deactivates it (the list's per-row menu and its
/// "Show Inactive" toggle). <see cref="Domain.Tenancy.BillingLocation.Update"/> refuses to deactivate
/// the HeadOffice row; nothing here can change a location's type.
/// </summary>
public sealed record UpdateBillingLocationCommand(
    Guid OrganizationId,
    Guid Id,
    string Code,
    string Name,
    string? Address,
    Guid? WarehouseId,
    bool IsActive)
    : IRequest, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BillingLocationManage;
}
