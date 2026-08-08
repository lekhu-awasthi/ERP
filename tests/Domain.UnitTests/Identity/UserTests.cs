using ErpApp.Domain.Identity;

namespace ErpApp.Domain.UnitTests.Identity;

public class UserTests
{
    [Fact]
    public void Register_starts_as_email_unverified_with_lowercased_email()
    {
        var user = User.Register("Jane Doe", "Jane.Doe@Example.com", "9800000000", "hashed");

        Assert.Equal(UserStatus.EmailUnverified, user.Status);
        Assert.Equal("jane.doe@example.com", user.Email);
    }

    [Fact]
    public void MarkEmailVerified_transitions_unverified_user_to_active()
    {
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");

        user.MarkEmailVerified();

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void MarkEmailVerified_is_idempotent_for_an_already_active_user()
    {
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        user.MarkEmailVerified();

        user.MarkEmailVerified();

        Assert.Equal(UserStatus.Active, user.Status);
    }
}
