using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetMyOpenPosSession;

/// <summary>
/// Phase 61 -- the caller's open session at one till, or null: what a till asks first, to decide
/// between "Start Session" and the till itself. Phase 62's session picker is its consumer.
/// </summary>
public sealed record GetMyOpenPosSessionQuery(Guid OrganizationId, Guid LocationId)
    : IRequest<PosSessionDto?>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
