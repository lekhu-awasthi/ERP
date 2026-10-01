using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using MediatR;

namespace ErpApp.Application.Pos.Commands.ClosePosSession;

/// <summary>
/// Phase 61 -- closes the caller's drawer with the cash counted in it, note by note or as one amount
/// (a location that requires cash verification takes the count only). The difference from what the
/// shared reader says the drawer should hold posts to the tenant's Cash Over/Short account, and a
/// difference needs a note. Lock-date sensitive on today, because the over/short posts today.
///
/// <para><b>No feature gate</b>, unlike every other POS request. Closing a drawer ends an operation,
/// and phase 60's rule for <c>PosMode.None</c> is that switching something off needs no entitlement:
/// a tenant that loses its POS plan mid-shift must still be able to count and close the cash it is
/// holding. Nothing else is reachable through this -- there is no session to close without one
/// having been opened under the entitlement.</para>
/// </summary>
public sealed record ClosePosSessionCommand(
    Guid OrganizationId,
    Guid SessionId,
    decimal? CountedAmount,
    IReadOnlyList<DenominationCount>? Denominations,
    string? Note)
    : IRequest<PosSessionDto>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    /// <summary>The Nepal date the over/short posts on. Not bindable from a request body.</summary>
    public DateOnly Date { get; init; } = NepalTime.LocalDate(DateTimeOffset.UtcNow);
}
