using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Identity.Commands.RecordUserLoginEvent;
using ErpApp.Application.Identity.Queries.UserLog;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Identity;

/// <summary>
/// Phase 26c. The interesting behaviour is not the listing -- it is how a deliberately tenant-less
/// event becomes a tenant-scoped report, and in particular which failed attempts an Admin can see.
/// </summary>
public class UserLogQueryHandlerTests
{
    private const string WindowsChrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36";

    [Fact]
    public async Task A_recorded_login_is_parsed_into_the_reports_Device_and_Device_Info_columns()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, userId) = await SeedMemberAsync(db, "jane@example.com", "Jane Doe");

        await RecordAsync(db, userId, "Jane@Example.com", UserLoginOutcome.LoginSucceeded, WindowsChrome);

        var result = await Handle(db, organizationId);

        var row = Assert.Single(result.Items);
        Assert.Equal("Jane Doe", row.FullName);
        Assert.Equal("jane@example.com", row.Email);
        Assert.Equal("Windows 10", row.DeviceOs);
        Assert.Equal("Chrome 152.0.0.0", row.Browser);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("Login Success", row.Description);
    }

    [Theory]
    [InlineData(UserLoginOutcome.LoginSucceeded, "Login Success")]
    [InlineData(UserLoginOutcome.LoginFailed, "Login Fail")]
    [InlineData(UserLoginOutcome.LogoutSucceeded, "Logout Success")]
    public async Task Every_outcome_renders_the_reference_products_own_Description_wording(
        UserLoginOutcome outcome, string expected)
    {
        var db = TestAppDbContext.Create();
        var (organizationId, userId) = await SeedMemberAsync(db, "jane@example.com", "Jane Doe");

        await RecordAsync(db, userId, "jane@example.com", outcome, WindowsChrome);

        var result = await Handle(db, organizationId);

        Assert.Equal(expected, Assert.Single(result.Items).Description);
    }

    /// <summary>
    /// The half of the scoping rule that matters: a failed attempt has no user id, so it can only
    /// reach the right organization through the address that was tried. Without this an Admin would
    /// never see anyone attacking their colleagues' accounts.
    /// </summary>
    [Fact]
    public async Task A_failed_attempt_against_a_members_address_is_visible_even_though_it_has_no_user_id()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _) = await SeedMemberAsync(db, "jane@example.com", "Jane Doe");

        await RecordAsync(db, userId: null, "jane@example.com", UserLoginOutcome.LoginFailed, WindowsChrome);

        var result = await Handle(db, organizationId);

        var row = Assert.Single(result.Items);
        Assert.Null(row.UserId);
        Assert.Equal("Login Fail", row.Description);
        // No User row backs it, so the email stands in for the name -- the only honest answer.
        Assert.Equal("jane@example.com", row.FullName);
    }

    [Fact]
    public async Task A_failed_attempt_against_an_address_belonging_to_nobody_here_is_not_this_tenants_business()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _) = await SeedMemberAsync(db, "jane@example.com", "Jane Doe");

        await RecordAsync(db, userId: null, "stranger@elsewhere.test", UserLoginOutcome.LoginFailed, WindowsChrome);

        var result = await Handle(db, organizationId);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Another_organizations_member_never_appears()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _) = await SeedMemberAsync(db, "jane@example.com", "Jane Doe");
        var (_, otherUserId) = await SeedMemberAsync(db, "bob@example.com", "Bob Roe");

        await RecordAsync(db, otherUserId, "bob@example.com", UserLoginOutcome.LoginSucceeded, WindowsChrome);

        var result = await Handle(db, organizationId);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task The_user_filter_narrows_to_one_member_and_a_non_member_returns_nothing()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var jane = await AddMemberAsync(db, organizationId, "jane@example.com", "Jane Doe");
        var bob = await AddMemberAsync(db, organizationId, "bob@example.com", "Bob Roe");

        await RecordAsync(db, jane, "jane@example.com", UserLoginOutcome.LoginSucceeded, WindowsChrome);
        await RecordAsync(db, bob, "bob@example.com", UserLoginOutcome.LoginSucceeded, WindowsChrome);

        var unfiltered = await Handle(db, organizationId);
        var filtered = await Handle(db, organizationId, jane);
        var nonMember = await Handle(db, organizationId, Guid.NewGuid());

        Assert.Equal(2, unfiltered.Items.Count);
        Assert.Equal(jane, Assert.Single(filtered.Items).UserId);
        Assert.Empty(nonMember.Items);
    }

    [Fact]
    public async Task Events_outside_the_period_are_excluded_and_the_rest_come_back_newest_first()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, userId) = await SeedMemberAsync(db, "jane@example.com", "Jane Doe");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        db.UserLoginEvents.Add(EventAt(userId, "jane@example.com", today.AddDays(-1)));
        db.UserLoginEvents.Add(EventAt(userId, "jane@example.com", today));
        db.UserLoginEvents.Add(EventAt(userId, "jane@example.com", today.AddDays(-40)));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationId, from: today.AddDays(-7), to: today);

        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].OccurredAt > result.Items[1].OccurredAt);
    }

    private static UserLoginEvent EventAt(Guid userId, string email, DateOnly date) =>
        UserLoginEvent.Create(
            userId, email, UserLoginOutcome.LoginSucceeded,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            "203.0.113.7", WindowsChrome, "Windows 10", "Chrome 152.0.0.0");

    private static async Task RecordAsync(
        IAppDbContext db, Guid? userId, string email, UserLoginOutcome outcome, string userAgent) =>
        await new RecordUserLoginEventCommandHandler(db).Handle(
            new RecordUserLoginEventCommand(userId, email, outcome, "203.0.113.7", userAgent),
            CancellationToken.None);

    private static Task<UserLogDto> Handle(
        IAppDbContext db, Guid organizationId, Guid? userId = null, DateOnly? from = null, DateOnly? to = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new UserLogQueryHandler(db).Handle(
            new UserLogQuery(
                organizationId, from ?? today.AddDays(-7), to ?? today.AddDays(1), userId),
            CancellationToken.None);
    }

    private static async Task<(Guid OrganizationId, Guid UserId)> SeedMemberAsync(
        IAppDbContext db, string email, string fullName)
    {
        var organizationId = Guid.NewGuid();
        var userId = await AddMemberAsync(db, organizationId, email, fullName);
        return (organizationId, userId);
    }

    private static async Task<Guid> AddMemberAsync(
        IAppDbContext db, Guid organizationId, string email, string fullName)
    {
        var user = User.Register(fullName, email, "9800000000", "hashed");
        db.Users.Add(user);
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, user.Id, MembershipRole.Admin));
        await db.SaveChangesAsync(CancellationToken.None);
        return user.Id;
    }
}
