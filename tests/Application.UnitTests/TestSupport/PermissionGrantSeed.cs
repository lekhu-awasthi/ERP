using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// Phase 27a -- seeds an Accepted membership plus the role grants a handler's <i>own</i> permission
/// check will look for.
///
/// <para>Almost every handler in this codebase is gated only by <c>AuthorizationBehavior</c>, which
/// unit tests bypass by calling the handler directly -- so the tests never needed a membership at
/// all. The two attachment handlers addressed by id alone are the exception: their real gate had to
/// move inside the handler (their key depends on a column of the row they are about to read), so a
/// test that calls one directly must seed the grant or get a Forbidden.</para>
///
/// <para>That is a feature, not friction: it means the per-parent gate is exercised by every
/// attachment test rather than only by the one test written to prove it.</para>
/// </summary>
public static class PermissionGrantSeed
{
    /// <summary>
    /// Makes <paramref name="userId"/> an Accepted Admin of <paramref name="organizationId"/>
    /// holding exactly <paramref name="grantedKeys"/>.
    ///
    /// <para>Grants land on the system Admin role, because <see cref="OrganizationMembership"/> has
    /// no factory taking an arbitrary RoleId -- so one call per (organization, user) per test. To
    /// give two users different key sets in the same test, use two organizations.</para>
    /// </summary>
    public static async Task GrantAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid userId,
        params string[] grantedKeys)
    {
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, userId, MembershipRole.Admin));

        foreach (var key in grantedKeys)
        {
            db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, key, true));
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }
}
