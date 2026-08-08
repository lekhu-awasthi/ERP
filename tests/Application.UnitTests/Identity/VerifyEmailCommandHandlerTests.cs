using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Identity.Commands.VerifyEmail;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;

namespace ErpApp.Application.UnitTests.Identity;

public class VerifyEmailCommandHandlerTests
{
    [Fact]
    public async Task Handle_activates_user_for_matching_code()
    {
        var db = TestAppDbContext.Create();
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        var code = VerificationCode.Issue(user.Id, "123456", VerificationCodePurpose.EmailVerification, TimeSpan.FromMinutes(15));
        db.Users.Add(user);
        db.VerificationCodes.Add(code);
        await db.SaveChangesAsync();

        await new VerifyEmailCommandHandler(db).Handle(
            new VerifyEmailCommand("jane@example.com", "123456"), CancellationToken.None);

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public async Task Handle_throws_for_wrong_code()
    {
        var db = TestAppDbContext.Create();
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        var code = VerificationCode.Issue(user.Id, "123456", VerificationCodePurpose.EmailVerification, TimeSpan.FromMinutes(15));
        db.Users.Add(user);
        db.VerificationCodes.Add(code);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidVerificationCodeException>(() => new VerifyEmailCommandHandler(db).Handle(
            new VerifyEmailCommand("jane@example.com", "000000"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_email()
    {
        var db = TestAppDbContext.Create();

        await Assert.ThrowsAsync<NotFoundException>(() => new VerifyEmailCommandHandler(db).Handle(
            new VerifyEmailCommand("nobody@example.com", "123456"), CancellationToken.None));
    }
}
