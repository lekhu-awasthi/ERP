using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Commands.Login;

public sealed class LoginCommandHandler(IAppDbContext db, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new AuthenticationFailedException("Invalid email or password.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new EmailNotVerifiedException("Please verify your email before logging in.");
        }

        var token = jwtTokenGenerator.GenerateToken(user.Id, user.Email);

        return new LoginResult(user.Id, user.Email, user.FullName, token.Value, token.ExpiresAt);
    }
}
