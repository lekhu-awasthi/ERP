using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.RecordPosCashMovement;

/// <summary>
/// Phase 61 -- Cash In or Cash Out of the caller's open drawer, naming the account on the other side,
/// posted at once (phase 59 Decision H; the vendor's defect 6 is that it does not post).
///
/// <para>Lock-date sensitive on today's date, because it posts today: a till cannot move money
/// through a closed period any more than a journal voucher can. A Cash Out is subject to the
/// tenant's Negative Cash Balance setting, like every other outflow from a cash account (phase 31),
/// with the same 422-then-override shape.</para>
/// </summary>
public sealed record RecordPosCashMovementCommand(
    Guid OrganizationId,
    Guid SessionId,
    PosCashMovementDirection Direction,
    decimal Amount,
    Guid AccountId,
    string? Note,
    bool OverrideNegativeCashBalanceWarning = false)
    : IRequest<PosSessionDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;

    /// <summary>The Nepal date the movement posts on. Not bindable from a request body: a drawer
    /// moves cash now.</summary>
    public DateOnly Date { get; init; } = NepalTime.LocalDate(DateTimeOffset.UtcNow);
}
