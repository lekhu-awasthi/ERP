using ErpApp.Domain.Identity;
using MediatR;

namespace ErpApp.Application.Identity.Commands.RecordUserLoginEvent;

/// <summary>
/// Writes one <see cref="UserLoginEvent"/>. Phase 26c.
///
/// <para><b>Why this is sent from the endpoint rather than written inside
/// <c>LoginCommandHandler</c>.</b> Three reasons, and each one alone would be enough. (1) The
/// failed-login row has to be written on a path that <i>throws</i> --
/// <c>AuthenticationFailedException</c> is how a bad password is reported -- and a handler that
/// saved a row and then threw would be making its own failure path transactional in a way nothing
/// else in this codebase is. (2) Logout has no handler at all; it is a cookie deletion in
/// <c>AuthEndpoints</c>. (3) IP address and User-Agent live on <c>HttpContext</c>, which the
/// Application layer deliberately cannot see. Putting the decision in the endpoint keeps all three
/// events in one visible place -- which is also, literally, what the roadmap asks for: a row
/// "written by the auth endpoints".</para>
///
/// <para><b>No <c>IRequirePermission</c>, and no <c>IOrganizationScoped</c>.</b> Two of the three
/// events happen while nobody is authenticated, so there is no membership to check and no
/// organization to scope to; CLAUDE.md's "every <c>IOrganizationScoped</c> request must implement
/// <c>IRequirePermission</c>" rule is satisfied vacuously because this request is neither. The
/// exposure control lives on the <i>read</i> side, where <c>UserLogQuery</c> is Admin-only. This
/// mirrors <c>RegisterUserCommand</c> and <c>LoginCommand</c>, which are also unauthenticated
/// writes.</para>
///
/// <para><paramref name="UserAgent"/> arrives raw; the handler parses it into the report's Device
/// and Device Info columns via <see cref="UserAgentReader"/>.</para>
/// </summary>
public sealed record RecordUserLoginEventCommand(
    Guid? UserId,
    string Email,
    UserLoginOutcome Outcome,
    string? IpAddress,
    string? UserAgent) : IRequest<Unit>;
