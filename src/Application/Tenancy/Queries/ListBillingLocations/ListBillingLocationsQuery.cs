using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListBillingLocations;

/// <summary>
/// The Billing Location table on Organization &gt; Features -- Code, Name, Address, Warehouse -- and
/// the source of every document header's location picker.
///
/// <para>Its own query rather than the generic <c>ListLookupsQuery&lt;T&gt;</c>: that projects Name
/// and IsActive only, and every consumer here needs Code (the picker renders <c>Name (Code)</c>), the
/// address and the warehouse. See <see cref="BillingLocation"/>'s remarks.</para>
///
/// <para><see cref="IncludeInactive"/> is the list's "Show Inactive" checkbox. It defaults false, so a
/// document picker gets active locations only without having to know to ask.</para>
/// </summary>
public sealed record ListBillingLocationsQuery(Guid OrganizationId, bool IncludeInactive = false)
    : IRequest<IReadOnlyList<BillingLocationDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BillingLocationView;
}

public sealed record BillingLocationDto(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    Guid? WarehouseId,
    string? WarehouseName,
    BillingLocationType LocationType,
    bool IsHeadOffice,
    bool IsActive);
