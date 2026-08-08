using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Commands.VerifyEmail;

public sealed class VerifyEmailCommandHandler(IAppDbContext db) : IRequestHandler<VerifyEmailCommand>
{
    public async Task Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("No account found with this email.");

        if (user.Status == UserStatus.Active)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var verificationCode = await db.VerificationCodes
            .Where(c => c.UserId == user.Id && c.Purpose == VerificationCodePurpose.EmailVerification)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode is null || !verificationCode.IsValid(request.Code, now))
        {
            throw new InvalidVerificationCodeException("This verification code is invalid or has expired.");
        }

        verificationCode.Consume();
        user.MarkEmailVerified();

        await db.SaveChangesAsync(cancellationToken);
    }
}
