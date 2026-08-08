using System.IdentityModel.Tokens.Jwt;
using ErpApp.Application.Common.Security;

namespace ErpApp.Api.Services;

/// <summary>
/// Implements Application's ICurrentUserService over HttpContext -- lives in Api (the
/// composition root) rather than Infrastructure, since it depends on ASP.NET Core's request
/// pipeline, not a persistence/external-service concern.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Guid UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(value, out var userId))
            {
                throw new InvalidOperationException("No authenticated user in the current request context.");
            }

            return userId;
        }
    }
}
