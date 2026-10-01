using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosTills;

/// <summary>One till the caller could work at, and their own open session there if they have one.</summary>
public sealed record PosTillSummaryDto(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    PosMode PosMode,
    Guid? MySessionId,
    string? MySessionCode,
    DateTimeOffset? MySessionOpenedAt);

/// <summary>
/// Phase 62 -- the till's launcher: every location running a till that the caller may open a drawer
/// at. That is the rule <c>OpenPosSessionCommandHandler</c> already enforces -- the location is
/// active, runs a till the tenant is still entitled to, and the caller holds
/// <c>Sales.Invoice.Create</c> there (phase 61 Decision F) -- applied as a filter, so a cashier
/// scoped to one branch is shown one branch instead of being offered four and refused three.
///
/// <para>Gated on <c>Pos.Session.Operate</c> rather than the ERP's settings key: the settings read
/// (<c>Pos.Settings.Manage</c>) is Admin-only, and a Member cashier has to be able to find their till
/// (phase-62-status.md Decision D).</para>
/// </summary>
public sealed record ListPosTillsQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<PosTillSummaryDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
