using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.CreateBillingLocation;

/// <summary>
/// Adds one billing location -- the "Add New Location" dialog confirmed live 2026-09-07 on
/// Organization &gt; Features: <b>Location Code*, Location Name*, Address*, Warehouse*</b> and Save,
/// with no location-type control of any kind (see <see cref="Domain.Tenancy.BillingLocationType"/>).
///
/// <para><see cref="WarehouseId"/> is optional here although the live dialog marks it required,
/// because nothing seeds a Warehouse for a tenant (phase-20f Decision #4) -- requiring it would make
/// the second location unreachable for a tenant that has never created one, which is the phase-20f
/// failure mode itself. The Angular form marks it required whenever the tenant has any warehouse to
/// pick.</para>
/// </summary>
public sealed record CreateBillingLocationCommand(
    Guid OrganizationId,
    string Code,
    string Name,
    string? Address = null,
    Guid? WarehouseId = null)
    : IRequest<CreateBillingLocationResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BillingLocationManage;
}

public sealed record CreateBillingLocationResult(Guid Id, string Code, string Name);
