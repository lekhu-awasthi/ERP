using ErpApp.Application.Common.Security;
using ErpApp.Application.Platform.Commands.SetUserPreference;
using MediatR;

namespace ErpApp.Application.Platform.Queries.GetUserPreferences;

/// <summary>
/// Phase 33 -- every setting the acting user has made in this organization, in one round trip.
///
/// <para><b>All of them, not one at a time.</b> The client needs the calendar choice before its
/// first paint and the Quick Links tray as soon as Home renders; two requests for two small rows
/// would just be two chances to be slow. As the vocabulary grows this stays one request.</para>
///
/// <para>Like its write side, it takes no user id -- see <c>SetUserPreferenceCommand</c>'s remarks.
/// A user who has never set anything gets an empty list, not a 404: "no preferences" is a normal
/// state, and every consumer already has to have a default for the key it cares about.</para>
/// </summary>
public sealed record GetUserPreferencesQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<UserPreferenceDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.UserPreferenceManage;
}
