using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErpApp.Application.Identity.Commands.ForgotPassword;
using ErpApp.Application.Identity.Commands.Login;
using ErpApp.Application.Identity.Commands.RecordUserLoginEvent;
using ErpApp.Application.Identity.Commands.RegisterUser;
using ErpApp.Application.Identity.Commands.RequestVerificationCode;
using ErpApp.Application.Identity.Commands.ResetPassword;
using ErpApp.Application.Identity.Commands.VerifyEmail;
using ErpApp.Domain.Identity;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class AuthEndpoints
{
    public const string AuthCookieName = "erp_auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new RegisterUserCommand(request.FullName, request.Email, request.Phone, request.Password, request.TurnstileToken), ct);
            return Results.Created($"/api/auth/{result.UserId}", result);
        });

        group.MapPost("/request-verification-code", async (RequestVerificationCodeRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RequestVerificationCodeCommand(request.Email), ct);
            return Results.Ok(new { message = "Verification code sent." });
        });

        group.MapPost("/verify-email", async (VerifyEmailRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new VerifyEmailCommand(request.Email, request.Code), ct);
            return Results.Ok(new { message = "Email verified." });
        });

        group.MapPost("/login", async (
            LoginRequest request, ISender sender, HttpContext http, HttpResponse response, CancellationToken ct) =>
        {
            LoginResult result;
            try
            {
                result = await sender.Send(new LoginCommand(request.Email, request.Password), ct);
            }
            catch
            {
                // Phase 26c. The failed attempt is the row the User Log exists for, and it is
                // written here rather than in LoginCommandHandler because this is the only place
                // that can see the throw -- see RecordUserLoginEventCommand's remarks. It is
                // written for *any* failure the command reports (wrong password, unknown address,
                // unverified email): from a log reader's point of view they are one event, "this
                // address tried and did not get in", and distinguishing them in the report would
                // disclose which addresses exist.
                await RecordAsync(sender, http, userId: null, request.Email, UserLoginOutcome.LoginFailed, ct);
                throw;
            }

            // SameSite=None (not Lax): the Angular dev server runs on http://localhost:4200
            // while the Api runs on https://localhost:7104 -- different schemes make these
            // "cross-site" under Chrome's schemeful-same-site rules even though the host
            // matches, so Lax would silently drop the cookie on every cross-origin fetch.
            // None requires Secure, which is already set.
            response.Cookies.Append(AuthCookieName, result.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = result.ExpiresAt,
                Path = "/",
            });

            await RecordAsync(sender, http, result.UserId, result.Email, UserLoginOutcome.LoginSucceeded, ct);

            return Results.Ok(new { result.UserId, result.Email, result.FullName });
        });

        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ForgotPasswordCommand(request.Email), ct);
            return Results.Ok(new { message = "Password reset code sent." });
        });

        group.MapPost("/reset-password", async (ResetPasswordRequest request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResetPasswordCommand(request.Email, request.Code, request.NewPassword), ct);
            return Results.Ok(new { message = "Password has been reset." });
        });

        group.MapPost("/logout", async (
            ClaimsPrincipal user, ISender sender, HttpContext http, HttpResponse response, CancellationToken ct) =>
        {
            // UseAuthentication populates HttpContext.User from the cookie whether or not the
            // endpoint requires authorization, so a real logout is identified here; a logout posted
            // without a valid cookie records nothing, because there is no session to have ended.
            var email = user.FindFirstValue(JwtRegisteredClaimNames.Email);
            if (email is not null)
            {
                var userId = Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var parsed)
                    ? parsed
                    : (Guid?)null;
                await RecordAsync(sender, http, userId, email, UserLoginOutcome.LogoutSucceeded, ct);
            }

            // Browsers only overwrite/expire a cookie when Path/Secure/SameSite match how it
            // was set (see /login above) -- Delete() with just Path left the original cookie
            // (Secure; SameSite=None) untouched, so /me kept succeeding after "logout".
            response.Cookies.Delete(AuthCookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
            });
            return Results.Ok();
        });

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = user.FindFirstValue(JwtRegisteredClaimNames.Email);
            return Results.Ok(new { userId, email });
        }).RequireAuthorization();
    }

    /// <summary>
    /// Phase 26c -- writes one User Log row. IP address and User-Agent are read here because
    /// <c>HttpContext</c> is deliberately invisible to the Application layer; the raw agent string
    /// is parsed into the report's Device/Device Info columns by the command's handler.
    ///
    /// <para>Recording is <b>never allowed to break authentication</b>. A login that succeeded has
    /// succeeded, and a logout that cleared the cookie has logged the user out, whatever happened
    /// to the audit row -- so a failure here is swallowed rather than propagated. That is the
    /// opposite of the call <c>AuditBehavior</c> makes, and deliberately: this row sits on the
    /// unauthenticated edge of the system, where a write failure must not become a way to deny
    /// someone their session.</para>
    /// </summary>
    private static async Task RecordAsync(
        ISender sender, HttpContext http, Guid? userId, string email, UserLoginOutcome outcome, CancellationToken ct)
    {
        try
        {
            await sender.Send(
                new RecordUserLoginEventCommand(
                    userId,
                    email,
                    outcome,
                    http.Connection.RemoteIpAddress?.ToString(),
                    http.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null),
                ct);
        }
        catch (Exception ex)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthEndpoints))
                .LogError(ex, "Failed to record a {Outcome} user login event.", outcome);
        }
    }

    private sealed record RegisterRequest(string FullName, string Email, string Phone, string Password, string TurnstileToken);

    private sealed record RequestVerificationCodeRequest(string Email);

    private sealed record VerifyEmailRequest(string Email, string Code);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record ForgotPasswordRequest(string Email);

    private sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
}
