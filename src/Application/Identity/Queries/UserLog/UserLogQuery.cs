using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Identity;
using MediatR;

namespace ErpApp.Application.Identity.Queries.UserLog;

/// <summary>
/// The System Report group's <b>User Log</b> (phase 26c). Read live on 2026-09-03: filters Period
/// and User; columns Full Name, Email, Date, Device, IP Address, Description, Device Info; newest
/// first.
///
/// <para><b>Admin-only</b>, and not by the standing rule's usual "does it name a contact" test --
/// by a stronger one. This report discloses, per person, where they were (IP address) and what they
/// were using (device and browser) at a given minute, plus the addresses that failed to sign in.
/// That is surveillance-grade data about colleagues rather than commercial data about the business,
/// and the only role that should hold it is the one that administers the organization.
/// <c>SystemAuditView</c> is the nearest precedent and is Admin-only for a weaker reason.</para>
///
/// <para><b>How a tenant-less event becomes a tenant-scoped report.</b> <c>UserLoginEvent</c>
/// deliberately stores no <c>OrganizationId</c> (signing in happens before an organization is
/// chosen). The handler scopes instead by <c>OrganizationMembership</c>, in two parts: events whose
/// <c>UserId</c> is a member of this organization, <b>plus</b> events with no user id whose
/// attempted email matches a member's. The second half is what makes a failed attempt against a
/// colleague's address visible to their Admin. An attempt against an address belonging to nobody in
/// this organization is deliberately invisible here -- it is not this tenant's business, and
/// showing it would leak the existence of other tenants' users.</para>
/// </summary>
public sealed record UserLogQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? UserId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<UserLogDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.UserLogView;
}

/// <summary>
/// <paramref name="FullName"/> falls back to the email when no <c>User</c> row backs the event --
/// which is both what the live report does for a user with no name set and the only thing it can
/// do for a failed attempt that never resolved to a user.
/// </summary>
public sealed record UserLogRowDto(
    Guid Id,
    Guid? UserId,
    string FullName,
    string Email,
    DateTimeOffset OccurredAt,
    string? DeviceOs,
    string? IpAddress,
    UserLoginOutcome Outcome,
    string Description,
    string? Browser);

public sealed record UserLogDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<UserLogRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
