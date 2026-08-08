using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErpApp.Application.Identity.Commands.ForgotPassword;
using ErpApp.Application.Identity.Commands.Login;
using ErpApp.Application.Identity.Commands.RegisterUser;
using ErpApp.Application.Identity.Commands.RequestVerificationCode;
using ErpApp.Application.Identity.Commands.ResetPassword;
using ErpApp.Application.Identity.Commands.VerifyEmail;
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
                new RegisterUserCommand(request.FullName, request.Email, request.Phone, request.Password), ct);
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

        group.MapPost("/login", async (LoginRequest request, ISender sender, HttpResponse response, CancellationToken ct) =>
        {
            var result = await sender.Send(new LoginCommand(request.Email, request.Password), ct);

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

        group.MapPost("/logout", (HttpResponse response) =>
        {
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

    private sealed record RegisterRequest(string FullName, string Email, string Phone, string Password);

    private sealed record RequestVerificationCodeRequest(string Email);

    private sealed record VerifyEmailRequest(string Email, string Code);

    private sealed record LoginRequest(string Email, string Password);

    private sealed record ForgotPasswordRequest(string Email);

    private sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
}
