using System.IdentityModel.Tokens.Jwt;
using ErpApp.Application.Common.Security;

namespace ErpApp.Api.Services;

/// <summary>
/// Implements Application's ICurrentUserService over HttpContext -- lives in Api (the
/// composition root) rather than Infrastructure, since it depends on ASP.NET Core's request
/// pipeline, not a persistence/external-service concern.
///
/// <para><b>Phase 21a added the background-job fallback, and the order of the two checks is the
/// security property.</b> Until then this threw whenever there was no HTTP context, which is what
/// let Phase 20e state that no ambient identity existed anywhere. A bulk import writes through the
/// ordinary Create/Update commands and therefore needs an acting user, so
/// <see cref="IJobActingUser"/> supplies one -- but only where there is no request at all.</para>
///
/// <para>An <c>HttpContext</c> present means the JWT decides, full stop: a request with a malformed
/// or missing <c>sub</c> claim still throws rather than falling through to the job identity. That
/// is what makes it impossible for a background identity to serve a real request, even in a scope
/// where something had called <c>Assume</c>. Outside a request there is no principal to prefer, so
/// the job's own recorded initiator is used, and it is null (and this still throws) in every scope
/// that is not a job's.</para>
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor, IJobActingUser jobActingUser)
    : ICurrentUserService
{
    public Guid UserId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;

            if (httpContext is not null)
            {
                var value = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                return Guid.TryParse(value, out var userId)
                    ? userId
                    : throw new InvalidOperationException("No authenticated user in the current request context.");
            }

            return jobActingUser.UserId
                ?? throw new InvalidOperationException(
                    "No authenticated user in the current request context, and no background job has assumed one.");
        }
    }
}
