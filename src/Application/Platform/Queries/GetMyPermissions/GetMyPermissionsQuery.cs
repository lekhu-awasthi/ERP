using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Platform.Queries.GetMyPermissions;

/// <summary>
/// The permission keys the acting user holds in this organization, so the shell can stop offering
/// screens the server would refuse. The left nav, the screen half of global search, the Quick Links
/// picker, the Create New flyout and the Reports index all used to list every route for every user,
/// so a restricted role clicked into a page that then showed a wall of 403s.
///
/// <para><b>This is a usability read, never an authorization one.</b> Hiding a link refuses
/// nothing; <c>AuthorizationBehavior</c> is still the only thing that does. The client therefore
/// fails <i>open</i> when this read fails: it shows the full nav and lets the server answer.</para>
///
/// <para><b>Gated on <see cref="PermissionKeys.SubscriptionView"/>, not a key of its own.</b> Phase
/// 20f granted that key to Admin and Member for exactly this job: "the Angular shell reads this
/// query to decide which feature-gated nav entries to render, so every role needs it". The nav now
/// also asks which permission-gated entries to render, from the same shell, for the same reason.
/// A second key would only add a second way for one role to lose its nav. Reading your own grants
/// gives nothing the user could not learn by trying each screen, so borrowing the key widens no
/// power (the phase-76 trap is a key that <i>does</i> something).</para>
///
/// <para>Like <c>GetUserPreferencesQuery</c> it takes no user id: it answers only for the caller.</para>
/// </summary>
public sealed record GetMyPermissionsQuery(Guid OrganizationId)
    : IRequest<MyPermissionsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SubscriptionView;
}

/// <param name="OrganizationWide">Keys granted across every billing location.</param>
/// <param name="AtSomeLocation">
/// Keys granted only at one or more billing locations (phase 32b), and not organization-wide. A
/// screen is worth offering for these too: <c>AuthorizationBehavior</c> lets a list through on a key
/// held at any one location and narrows its rows to those locations.
/// </param>
public sealed record MyPermissionsDto(IReadOnlyList<string> OrganizationWide, IReadOnlyList<string> AtSomeLocation);
