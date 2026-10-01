using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.OpenPosSession;

/// <summary>
/// Phase 61 -- opens the caller's session at one till, with its float counted either note by note
/// (<paramref name="Denominations"/>) or as one amount (<paramref name="OpeningAmount"/>), as the
/// vendor's Start Session offers both. A location that requires cash verification takes the count
/// only. Posts nothing (see <see cref="PosSession"/>).
/// </summary>
public sealed record OpenPosSessionCommand(
    Guid OrganizationId,
    Guid LocationId,
    decimal? OpeningAmount,
    IReadOnlyList<DenominationCount>? Denominations)
    : IRequest<PosSessionDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
