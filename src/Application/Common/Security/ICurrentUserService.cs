namespace ErpApp.Application.Common.Security;

/// <summary>
/// The acting user for the current request, resolved from the JWT (Api layer implements this
/// over HttpContext, since Application can't depend on ASP.NET Core hosting types). Every
/// Tenancy command that needs to know "who is doing this" (create/invite/accept) depends on
/// this rather than taking a UserId parameter the client could forge for someone else.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }
}
