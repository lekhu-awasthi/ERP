using ErpApp.Domain.Identity;

namespace ErpApp.Domain.UnitTests.Identity;

public class UserLoginEventTests
{
    private static UserLoginEvent Create(
        Guid? userId = null,
        string email = "jane@example.com",
        UserLoginOutcome outcome = UserLoginOutcome.LoginSucceeded,
        string? userAgent = null) =>
        UserLoginEvent.Create(
            userId, email, outcome, DateTimeOffset.UtcNow, "127.0.0.1", userAgent, "Windows 10", "Chrome 152.0.0.0");

    [Fact]
    public void Create_normalises_the_email_the_way_the_login_handler_does()
    {
        var loginEvent = Create(email: "  Jane.Doe@Example.COM ");

        Assert.Equal("jane.doe@example.com", loginEvent.Email);
    }

    [Fact]
    public void Create_allows_a_null_user_so_a_failed_attempt_still_records_the_address_tried()
    {
        var loginEvent = Create(userId: null, email: "nobody@example.com", outcome: UserLoginOutcome.LoginFailed);

        Assert.Null(loginEvent.UserId);
        Assert.Equal("nobody@example.com", loginEvent.Email);
        Assert.Equal(UserLoginOutcome.LoginFailed, loginEvent.Outcome);
    }

    [Fact]
    public void Create_rejects_an_event_with_no_email_because_the_address_is_the_only_certain_fact()
    {
        Assert.Throws<InvalidOperationException>(() => Create(email: "   "));
    }

    [Fact]
    public void Create_truncates_a_user_agent_longer_than_the_column_so_a_hostile_header_cannot_widen_the_row()
    {
        var loginEvent = Create(userAgent: new string('x', UserLoginEvent.UserAgentMaxLength + 500));

        Assert.Equal(UserLoginEvent.UserAgentMaxLength, loginEvent.UserAgent!.Length);
    }

    [Fact]
    public void Create_keeps_a_user_agent_that_fits_exactly_as_it_was_sent()
    {
        const string agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/152.0.0.0";

        var loginEvent = Create(userAgent: agent);

        Assert.Equal(agent, loginEvent.UserAgent);
    }
}
