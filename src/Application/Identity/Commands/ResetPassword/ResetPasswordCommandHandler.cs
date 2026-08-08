using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler(IAppDbContext db, IPasswordHasher passwordHasher)
    : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("No account found with this email.");

        var now = DateTimeOffset.UtcNow;
        var verificationCode = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Purpose == VerificationCodePurpose.PasswordReset)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode is null || !verificationCode.IsValid(request.Code, now))
        {
            throw new InvalidVerificationCodeException("This password reset code is invalid or has expired.");
        }

        verificationCode.Consume();
        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword));

        await db.SaveChangesAsync(cancellationToken);
    }
}
