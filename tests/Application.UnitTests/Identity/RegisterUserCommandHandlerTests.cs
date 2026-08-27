using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Identity.Commands.RegisterUser;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Identity;

public class RegisterUserCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_user_with_hashed_password_and_email_unverified_status()
    {
        var db = TestAppDbContext.Create();
        var handler = new RegisterUserCommandHandler(db, new FakePasswordHasher(), new FakeTurnstileVerifier());

        var result = await handler.Handle(
            new RegisterUserCommand("Jane Doe", "jane@example.com", "9800000000", "Password123", "turnstile-token"),
            CancellationToken.None);

        var user = await db.Users.SingleAsync(u => u.Id == result.UserId);
        Assert.Equal("jane@example.com", user.Email);
        Assert.Equal(UserStatus.EmailUnverified, user.Status);
        Assert.Equal("hashed:Password123", user.PasswordHash);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_email_already_registered()
    {
        var db = TestAppDbContext.Create();
        var handler = new RegisterUserCommandHandler(db, new FakePasswordHasher(), new FakeTurnstileVerifier());
        await handler.Handle(
            new RegisterUserCommand("Jane Doe", "jane@example.com", "9800000000", "Password123", "turnstile-token"),
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new RegisterUserCommand("Jane Two", "JANE@example.com", "9800000001", "Password456", "turnstile-token"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_when_turnstile_verification_fails()
    {
        var db = TestAppDbContext.Create();
        var handler = new RegisterUserCommandHandler(db, new FakePasswordHasher(), new FakeTurnstileVerifier(shouldSucceed: false));

        await Assert.ThrowsAsync<TurnstileVerificationFailedException>(() => handler.Handle(
            new RegisterUserCommand("Jane Doe", "jane@example.com", "9800000000", "Password123", "bad-token"),
            CancellationToken.None));

        Assert.False(await db.Users.AnyAsync(u => u.Email == "jane@example.com"));
    }
}
