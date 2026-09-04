using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.AcceptRequest;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.InviteUser;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Covers the "is this user allowed" check Phase 1c centralized here, replacing the ad hoc
/// checks InviteUserCommandHandler/AcceptRequestCommandHandler used to inline (see their
/// updated doc comments/tests).
/// </summary>
public class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Handle_calls_next_when_the_current_users_role_has_the_permission_granted()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var adminId = Guid.NewGuid();
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.OrganizationInviteUser, true));
        await db.SaveChangesAsync();

        var behavior = new AuthorizationBehavior<InviteUserCommand, InviteUserResult>(db, new FakeCurrentUserService(adminId));
        var command = new InviteUserCommand(organization.Id, "new.hire@example.com", Role.MemberId);
        var expected = new InviteUserResult(Guid.NewGuid(), "new.hire@example.com", Role.MemberId, "Member");

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Handle_throws_forbidden_when_the_current_users_role_has_the_permission_explicitly_denied()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var memberId = Guid.NewGuid();
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, memberId, MembershipRole.Member));
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.MemberId, PermissionKeys.OrganizationInviteUser, false));
        await db.SaveChangesAsync();

        var behavior = new AuthorizationBehavior<InviteUserCommand, InviteUserResult>(db, new FakeCurrentUserService(memberId));
        var command = new InviteUserCommand(organization.Id, "new.hire@example.com", Role.MemberId);

        await Assert.ThrowsAsync<ForbiddenException>(() => behavior.Handle(
            command, () => throw new InvalidOperationException("next() should not have been called."), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_forbidden_when_the_current_user_has_no_membership_in_the_organization_at_all()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var behavior = new AuthorizationBehavior<InviteUserCommand, InviteUserResult>(db, new FakeCurrentUserService(Guid.NewGuid()));
        var command = new InviteUserCommand(organization.Id, "new.hire@example.com", Role.MemberId);

        await Assert.ThrowsAsync<ForbiddenException>(() => behavior.Handle(
            command, () => throw new InvalidOperationException("next() should not have been called."), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_resolves_the_organization_from_the_target_membership_for_ITargetsMembership_requests()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var adminId = Guid.NewGuid();
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        var pendingRequest = OrganizationMembership.Request(organization.Id, Guid.NewGuid(), MembershipRole.Member);
        db.OrganizationMemberships.Add(pendingRequest);
        db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.OrganizationAcceptRequest, true));
        await db.SaveChangesAsync();

        var behavior = new AuthorizationBehavior<AcceptRequestCommand, Unit>(db, new FakeCurrentUserService(adminId));
        var command = new AcceptRequestCommand(pendingRequest.Id);
        var nextCalled = false;

        await behavior.Handle(command, () => { nextCalled = true; return Task.FromResult(Unit.Value); }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_the_targeted_membership_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var behavior = new AuthorizationBehavior<AcceptRequestCommand, Unit>(db, new FakeCurrentUserService(Guid.NewGuid()));

        await Assert.ThrowsAsync<NotFoundException>(() => behavior.Handle(
            new AcceptRequestCommand(Guid.NewGuid()),
            () => throw new InvalidOperationException("next() should not have been called."),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_allows_a_global_permission_request_for_any_authenticated_user()
    {
        // CreateOrganizationCommand implements IRequirePermission but not IOrganizationScoped or
        // ITargetsMembership -- it predates any membership in the Organization it's creating, so
        // this should pass through to next() with no Organization/Role/RolePermission rows at all.
        var db = TestAppDbContext.Create();
        var behavior = new AuthorizationBehavior<CreateOrganizationCommand, CreateOrganizationResult>(
            db, new FakeCurrentUserService(Guid.NewGuid()));
        var command = new CreateOrganizationCommand(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true, "acme-traders",
            null, null, null, null, false, false, false, false, false, false, false, "turnstile-token");
        var expected = new CreateOrganizationResult(Guid.NewGuid(), "Acme Traders", "acme-traders");

        var result = await behavior.Handle(command, () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
    }
}
