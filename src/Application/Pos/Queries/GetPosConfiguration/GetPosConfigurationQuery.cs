using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosConfiguration;

/// <summary>
/// Phase 60 -- the landing view of Configurations &gt; Point of Sale: which POS entitlements the
/// tenant has, its walk-in customer, and every location with its mode. A location is then opened
/// with <c>GetPosLocationSettingsQuery</c>.
///
/// <para>Gated on either POS entitlement: a tenant with neither has no till to configure and gets
/// the feature 403 naming both.</para>
/// </summary>
public sealed record GetPosConfigurationQuery(Guid OrganizationId)
    : IRequest<PosConfigurationDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSettingsManage;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed record PosConfigurationDto(
    bool PosRetailEnabled,
    bool PosRestaurantEnabled,
    PosWalkInCustomerDto? WalkInCustomer,
    IReadOnlyList<PosLocationSummaryDto> Locations);

public sealed record PosWalkInCustomerDto(Guid Id, string Code, string Name);

/// <param name="HasSettings">False when the location has never been saved and reads as the
/// defaults.</param>
public sealed record PosLocationSummaryDto(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    bool IsHeadOffice,
    PosMode PosMode,
    bool HasSettings,
    int LinkedPaymentModeCount);
