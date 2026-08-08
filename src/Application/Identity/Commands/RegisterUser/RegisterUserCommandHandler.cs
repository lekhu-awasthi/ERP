using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Commands.RegisterUser;

public sealed class RegisterUserCommandHandler(IAppDbContext db, IPasswordHasher passwordHasher)
    : IRequestHandler<RegisterUserCommand, RegisterUserResult>
{
    public async Task<RegisterUserResult> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailTaken = await db.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailTaken)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var passwordHash = passwordHasher.Hash(request.Password);
        var user = User.Register(request.FullName, request.Email, request.Phone, passwordHash);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        return new RegisterUserResult(user.Id, user.Email);
    }
}
