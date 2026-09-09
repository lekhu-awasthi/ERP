using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Platform.Commands.SetUserPreference;

/// <summary>
/// Phase 33 -- writes one of the acting user's own settings in one organization.
///
/// <para><b>There is no UserId on this command, deliberately.</b> The row is resolved from
/// <c>ICurrentUserService</c>, so the request cannot name someone else's preferences even by
/// accident, and no handler has to remember to check that the caller is who they claim. That is the
/// same reasoning <c>ICurrentUserService</c>'s own doc comment gives for why the Tenancy commands
/// don't take a user id either.</para>
///
/// <para><see cref="Value"/> is the setting serialized as JSON -- an array for the Quick Links tray,
/// a quoted string for the calendar choice. The Application layer validates the <see cref="Key"/>
/// against <see cref="UserPreferenceKeys.All"/> and the value's shape per key; the Domain treats it
/// as opaque text, for the reasons on <c>UserPreference</c> itself.</para>
/// </summary>
public sealed record SetUserPreferenceCommand(Guid OrganizationId, string Key, string Value)
    : IRequest<UserPreferenceDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.UserPreferenceManage;
}

public sealed record UserPreferenceDto(string Key, string Value, DateTimeOffset UpdatedAt);
