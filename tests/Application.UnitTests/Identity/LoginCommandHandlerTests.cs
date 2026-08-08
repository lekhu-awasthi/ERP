using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Identity.Commands.Login;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;

namespace ErpApp.Application.UnitTests.Identity;

public class LoginCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_token_for_correct_credentials()
    {
        var db = TestAppDbContext.Create();
        var hasher = new FakePasswordHasher();
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", hasher.Hash("Password123"));
        user.MarkEmailVerified();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, hasher, new FakeJwtTokenGenerator());

        var result = await handler.Handle(new LoginCommand("jane@example.com", "Password123"), CancellationToken.None);

        Assert.Equal("fake-token", result.Token);
        Assert.Equal(user.Id, result.UserId);
    }

    [Fact]
    public async Task Handle_throws_authentication_failed_for_wrong_password()
    {
        var db = TestAppDbContext.Create();
        var hasher = new FakePasswordHasher();
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", hasher.Hash("Password123"));
        user.MarkEmailVerified();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, hasher, new FakeJwtTokenGenerator());

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            handler.Handle(new LoginCommand("jane@example.com", "WrongPassword"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_email_not_verified_for_unverified_user()
    {
        var db = TestAppDbContext.Create();
        var hasher = new FakePasswordHasher();
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", hasher.Hash("Password123"));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LoginCommandHandler(db, hasher, new FakeJwtTokenGenerator());

        await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            handler.Handle(new LoginCommand("jane@example.com", "Password123"), CancellationToken.None));
    }
}
