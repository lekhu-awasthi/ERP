using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandHandler(IAppDbContext db, IEmailSender emailSender)
    : IRequestHandler<ForgotPasswordCommand>
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("No account found with this email.");

        var code = VerificationCodeGenerator.GenerateSixDigitCode();
        var verificationCode = VerificationCode.Issue(
            user.Id, code, VerificationCodePurpose.PasswordReset, CodeLifetime);

        db.VerificationCodes.Add(verificationCode);
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            user.Email,
            "Reset your ErpApp password",
            $"Your password reset code is {code}. It expires in 15 minutes.",
            cancellationToken);
    }
}
