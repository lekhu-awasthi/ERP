using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Platform;
using ErpApp.Application.Platform.Commands.SetUserPreference;
using ErpApp.Application.Platform.Queries.GetUserPreferences;
using ErpApp.Application.UnitTests.TestSupport;

namespace ErpApp.Application.UnitTests.Platform;

/// <summary>
/// Phase 33 -- the per-user store phase 23 declined to build for one boolean, and the two properties
/// that decision turns on: a preference belongs to one user (never to the tenant, and never to
/// whoever asks), and two different settings cannot clobber each other.
/// </summary>
public class UserPreferenceTests
{
    private const string Tray =
        """[{"name":"Invoices","area":"Sales","kind":"List","url":"/sales/invoices"}]""";

    [Fact]
    public async Task A_preference_round_trips_and_a_second_save_replaces_it()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SetAsync(db, organizationId, userId, UserPreferenceKeys.QuickLinks, Tray);
        await SetAsync(db, organizationId, userId, UserPreferenceKeys.QuickLinks, "[]");

        var stored = Assert.Single(await GetAsync(db, organizationId, userId));
        Assert.Equal(UserPreferenceKeys.QuickLinks, stored.Key);
        Assert.Equal("[]", stored.Value);
    }

    /// <summary>
    /// The whole reason this is a row per key rather than one JSON document per user: saving the
    /// tray must not discard the calendar choice, whatever order the two arrive in.
    /// </summary>
    [Fact]
    public async Task Two_different_keys_are_independent_rows_and_do_not_overwrite_each_other()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SetAsync(db, organizationId, userId, UserPreferenceKeys.Calendar, "\"BS\"");
        await SetAsync(db, organizationId, userId, UserPreferenceKeys.QuickLinks, Tray);

        var stored = await GetAsync(db, organizationId, userId);

        Assert.Equal(2, stored.Count);
        Assert.Equal("\"BS\"", stored.Single(x => x.Key == UserPreferenceKeys.Calendar).Value);
        Assert.Equal(Tray, stored.Single(x => x.Key == UserPreferenceKeys.QuickLinks).Value);
    }

    [Fact]
    public async Task One_users_preferences_are_invisible_to_another_and_to_another_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await SetAsync(db, organizationId, mine, UserPreferenceKeys.QuickLinks, Tray);

        Assert.Empty(await GetAsync(db, organizationId, theirs));
        Assert.Empty(await GetAsync(db, Guid.NewGuid(), mine));
    }

    [Theory]
    // The vocabulary is closed -- an unknown key is a 400, not a row.
    [InlineData("something-else", "[]", false)]
    // Quick Links shape.
    [InlineData(UserPreferenceKeys.QuickLinks, "[]", true)]
    [InlineData(UserPreferenceKeys.QuickLinks, "not json", false)]
    [InlineData(UserPreferenceKeys.QuickLinks, """{"name":"x"}""", false)]
    [InlineData(UserPreferenceKeys.QuickLinks, """[{"name":"","kind":"List","url":"/x"}]""", false)]
    // An absolute url in a stored navigation target is an open-redirect surface; so is a
    // protocol-relative one, and so is a backslash some browsers normalise to a slash.
    [InlineData(UserPreferenceKeys.QuickLinks, """[{"name":"x","kind":"List","url":"https://evil.example"}]""", false)]
    [InlineData(UserPreferenceKeys.QuickLinks, """[{"name":"x","kind":"List","url":"//evil.example"}]""", false)]
    [InlineData(UserPreferenceKeys.QuickLinks, """[{"name":"x","kind":"List","url":"/\\evil.example"}]""", false)]
    [InlineData(UserPreferenceKeys.QuickLinks, """[{"name":"x","kind":"List","url":"sales/invoices"}]""", false)]
    // Calendar shape.
    [InlineData(UserPreferenceKeys.Calendar, "\"BS\"", true)]
    [InlineData(UserPreferenceKeys.Calendar, "\"AD\"", true)]
    [InlineData(UserPreferenceKeys.Calendar, "\"Gregorian\"", false)]
    [InlineData(UserPreferenceKeys.Calendar, "BS", false)]
    public void The_validator_accepts_only_a_known_key_carrying_a_well_formed_value(
        string key, string value, bool expectedValid)
    {
        var result = new SetUserPreferenceCommandValidator()
            .Validate(new SetUserPreferenceCommand(Guid.NewGuid(), key, value));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void The_validator_refuses_a_tray_past_the_cap()
    {
        var links = string.Join(",", Enumerable.Range(0, SetUserPreferenceCommandValidator.MaxQuickLinks + 1)
            .Select(i => $$"""{"name":"L{{i}}","kind":"List","url":"/x/{{i}}"}"""));

        var result = new SetUserPreferenceCommandValidator().Validate(
            new SetUserPreferenceCommand(Guid.NewGuid(), UserPreferenceKeys.QuickLinks, $"[{links}]"));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Every key in the vocabulary must be reachable through the validator, or it is a constant
    /// nothing can ever write. The mirror of phase-31's lesson that a tenant field is only real if
    /// you can name the command that writes it.
    /// </summary>
    [Fact]
    public void Every_declared_key_has_a_value_the_validator_will_accept()
    {
        var samples = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserPreferenceKeys.QuickLinks] = "[]",
            [UserPreferenceKeys.Calendar] = "\"AD\"",
            [UserPreferenceKeys.DateRange] =
                """{"preset":"last-30","label":"Last 30 days","from":"2026-08-11","to":"2026-09-10"}""",
        };

        Assert.Equal(UserPreferenceKeys.All.OrderBy(x => x, StringComparer.Ordinal), samples.Keys.Order(StringComparer.Ordinal));

        foreach (var (key, value) in samples)
        {
            var result = new SetUserPreferenceCommandValidator()
                .Validate(new SetUserPreferenceCommand(Guid.NewGuid(), key, value));

            Assert.True(result.IsValid, $"{key} has no acceptable sample value.");
        }
    }

    [Fact]
    public void Both_requests_are_organization_scoped_and_permission_gated()
    {
        var set = new SetUserPreferenceCommand(Guid.NewGuid(), UserPreferenceKeys.Calendar, "\"AD\"");
        var get = new GetUserPreferencesQuery(Guid.NewGuid());

        // Every IOrganizationScoped request must implement IRequirePermission -- AuthorizationBehavior
        // is the only thing verifying org membership at all (phase 12's lesson).
        Assert.IsAssignableFrom<IOrganizationScoped>(set);
        Assert.IsAssignableFrom<IOrganizationScoped>(get);
        Assert.Equal(PermissionKeys.UserPreferenceManage, set.PermissionKey);
        Assert.Equal(PermissionKeys.UserPreferenceManage, get.PermissionKey);
    }

    private static Task SetAsync(
        IAppDbContext db, Guid organizationId, Guid userId, string key, string value) =>
        new SetUserPreferenceCommandHandler(db, new FakeCurrentUserService(userId))
            .Handle(new SetUserPreferenceCommand(organizationId, key, value), CancellationToken.None);

    private static async Task<IReadOnlyList<UserPreferenceDto>> GetAsync(
        IAppDbContext db, Guid organizationId, Guid userId) =>
        await new GetUserPreferencesQueryHandler(db, new FakeCurrentUserService(userId))
            .Handle(new GetUserPreferencesQuery(organizationId), CancellationToken.None);
}
