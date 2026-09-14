using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateOrganization;

/// <summary>
/// Phase 43 (39 carried item #2) -- the write path for <c>Configurations &gt; Organization
/// Profile</c>'s details, which phase 39 could only display.
///
/// <para><b>Why this existed as a gap at all.</b> <c>Organization</c> has been create-only since
/// phase 1b -- every field set once by the New Organization wizard and never again. Phase 39 built
/// the logo, showed the details beside it, and said on the page itself that editing them was not
/// available. That is phase-31's rule failing in its pure form: eight fields whose value is printed
/// on every customer-facing PDF, with no command that writes them and no screen that calls one.</para>
///
/// <para><b>What is not here.</b> <c>WorkspaceName</c>, the entitlement flags and <c>LockDate</c>
/// are all deliberately excluded, each for its own reason -- see <c>Organization.UpdateDetails</c>,
/// which states them where the invariant lives.</para>
///
/// <para><b>The key is <see cref="PermissionKeys.OrganizationProfileManage"/>, reused rather than
/// minted.</b> Phase 39 derived that key for exactly this screen and this bar: the details go out on
/// every customer-facing PDF, so changing them changes what every customer sees the organization as,
/// which is a decision about the business rather than daily working data. Admin-only, seeded, and
/// already the key the logo write uses on the same page -- a second key would mean two ways to be
/// denied on one form.</para>
/// </summary>
public sealed record UpdateOrganizationCommand(
    Guid OrganizationId,
    string Name,
    string Industry,
    string? Address,
    DateOnly AccountingStartDate,
    bool IsVatRegistered,
    string? Email,
    string? Phone,
    string? PanNumber,
    string? Website)
    : IRequest<UpdateOrganizationResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationProfileManage;
}

public sealed record UpdateOrganizationResult(Guid OrganizationId, string Name);
