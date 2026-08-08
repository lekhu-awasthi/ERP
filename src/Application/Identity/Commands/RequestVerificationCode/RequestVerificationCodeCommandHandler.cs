using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Commands.RequestVerificationCode;

public sealed class RequestVerificationCodeCommandHandler(IAppDbContext db, IEmailSender emailSender)
    : IRequestHandler<RequestVerificationCodeCommand>
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    public async Task Handle(RequestVerificationCodeCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("No account found with this email.");

        var code = VerificationCodeGenerator.GenerateSixDigitCode();
        var verificationCode = VerificationCode.Issue(
            user.Id, code, VerificationCodePurpose.EmailVerification, CodeLifetime);

        db.VerificationCodes.Add(verificationCode);
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            user.Email,
            "Verify your ErpApp account",
            $"Your verification code is {code}. It expires in 15 minutes.",
            cancellationToken);
    }
}
