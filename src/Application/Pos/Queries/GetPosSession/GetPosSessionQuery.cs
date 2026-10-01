using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosSession;

/// <summary>
/// Phase 61 -- one session as its X report (open) or Z report (closed). A cashier reads their own
/// with <see cref="PermissionKeys.PosSessionOperate"/>; anyone else's needs
/// <see cref="PermissionKeys.PosSessionViewAll"/>, re-checked in the handler because which key applies
/// depends on the row (the <c>AttachmentAccess</c> shape, phase 27a).
/// </summary>
public sealed record GetPosSessionQuery(Guid OrganizationId, Guid SessionId)
    : IRequest<PosSessionDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
