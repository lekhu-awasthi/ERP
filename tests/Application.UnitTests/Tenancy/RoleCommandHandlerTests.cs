using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.CreateRole;
using ErpApp.Application.Tenancy.Commands.DeleteRole;
using ErpApp.Application.Tenancy.Commands.UpdateMembershipRole;
using ErpApp.Application.Tenancy.Commands.UpdateRole;
using ErpApp.Application.Tenancy.Commands.UpdateRolePermissions;
using ErpApp.Application.Tenancy.Queries.GetRolePermissionMatrix;
using ErpApp.Application.Tenancy.Queries.ListRoles;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

public class RoleCommandHandlerTests
{
    [Fact]
    public async Task CreateRole_creates_a_custom_role_scoped_to_the_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        var handler = new CreateRoleCommandHandler(db);
        var result = await handler.Handle(new CreateRoleCommand(organizationId, "Sales Rep", "Sales-only access"), CancellationToken.None);

        var role = db.Roles.Single(r => r.Id == result.Id);
        Assert.Equal(organizationId, role.OrganizationId);
        Assert.Equal("Sales Rep", role.Name);
    }

    [Fact]
    public async Task CreateRole_throws_conflict_when_name_collides_with_a_system_role()
    {
        var db = TestAppDbContext.Create();
        db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        await db.SaveChangesAsync();

        var handler = new CreateRoleCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new CreateRoleCommand(Guid.NewGuid(), "Admin", null), CancellationToken.None));
    }

    [Fact]
    public async Task CreateRole_allows_the_same_name_in_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateRoleCommandHandler(db);
        await handler.Handle(new CreateRoleCommand(Guid.NewGuid(), "Sales Rep", null), CancellationToken.None);

        var result = await handler.Handle(new CreateRoleCommand(Guid.NewGuid(), "Sales Rep", null), CancellationToken.None);

        Assert.Equal("Sales Rep", result.Name);
    }

    [Fact]
    public async Task UpdateRole_throws_conflict_for_a_system_role()
    {
        var db = TestAppDbContext.Create();
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        await db.SaveChangesAsync();

        var handler = new UpdateRoleCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new UpdateRoleCommand(Guid.NewGuid(), Role.MemberId, "Renamed", null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRole_throws_not_found_when_the_role_belongs_to_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var owningOrgId = Guid.NewGuid();
        var role = Role.CreateCustom(owningOrgId, "Sales Rep");
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var handler = new UpdateRoleCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateRoleCommand(Guid.NewGuid(), role.Id, "Renamed", null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRole_updates_name_and_description_for_the_owning_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var role = Role.CreateCustom(organizationId, "Sales Rep");
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var handler = new UpdateRoleCommandHandler(db);
        var result = await handler.Handle(
            new UpdateRoleCommand(organizationId, role.Id, "Senior Sales Rep", "Updated"), CancellationToken.None);

        Assert.Equal("Senior Sales Rep", result.Name);
        Assert.Equal("Updated", result.Description);
    }

    [Fact]
    public async Task DeleteRole_throws_conflict_for_a_system_role()
    {
        var db = TestAppDbContext.Create();
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        await db.SaveChangesAsync();

        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new DeleteRoleCommand(Guid.NewGuid(), Role.MemberId), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_throws_conflict_when_still_referenced_by_a_membership()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var role = Role.CreateCustom(organizationId, "Sales Rep");
        db.Roles.Add(role);
        db.OrganizationMemberships.Add(
            OrganizationMembership.Invite(organizationId, userId: null, "rep@example.com", role.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var handler = new DeleteRoleCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new DeleteRoleCommand(organizationId, role.Id), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_removes_an_unreferenced_custom_role_and_its_permission_rows()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var role = Role.CreateCustom(organizationId, "Sales Rep");
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), role.Id, PermissionKeys.QuotationCreate, true));
        await db.SaveChangesAsync();

        var handler = new DeleteRoleCommandHandler(db);
        await handler.Handle(new DeleteRoleCommand(organizationId, role.Id), CancellationToken.None);

        Assert.False(db.Roles.Any(r => r.Id == role.Id));
        Assert.False(db.RolePermissions.Any(rp => rp.RoleId == role.Id));
    }

    [Fact]
    public async Task ListRoles_returns_system_rows_and_only_this_organizations_own_custom_rows()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        db.Roles.Add(Role.CreateCustom(organizationId, "Sales Rep"));
        db.Roles.Add(Role.CreateCustom(otherOrganizationId, "Other Org's Role"));
        await db.SaveChangesAsync();

        var handler = new ListRolesQueryHandler(db);
        var result = await handler.Handle(new ListRolesQuery(organizationId), CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Name == "Admin" && r.IsSystemRole);
        Assert.Contains(result, r => r.Name == "Member" && r.IsSystemRole);
        Assert.Contains(result, r => r.Name == "Sales Rep" && !r.IsSystemRole);
        Assert.DoesNotContain(result, r => r.Name == "Other Org's Role");
    }

    [Fact]
    public async Task GetRolePermissionMatrix_defaults_ungranted_keys_to_false_and_groups_by_module()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var role = Role.CreateCustom(organizationId, "Sales Rep");
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), role.Id, PermissionKeys.QuotationCreate, true));
        await db.SaveChangesAsync();

        var handler = new GetRolePermissionMatrixQueryHandler(db);
        var result = await handler.Handle(new GetRolePermissionMatrixQuery(organizationId, role.Id), CancellationToken.None);

        Assert.False(result.IsSystemRole);
        var salesGroup = result.Groups.Single(g => g.Module == "Sales");
        Assert.True(salesGroup.Permissions.Single(p => p.PermissionKey == PermissionKeys.QuotationCreate).IsGranted);
        Assert.False(salesGroup.Permissions.Single(p => p.PermissionKey == PermissionKeys.QuotationApprove).IsGranted);
        Assert.Equal(PermissionKeyCatalog.AllKeys.Count, result.Groups.Sum(g => g.Permissions.Count));
    }

    [Fact]
    public async Task UpdateRolePermissions_grants_a_key_revokes_a_key_and_touches_nothing_else()
    {
        var databaseName = Guid.NewGuid().ToString();
        var seedDb = TestAppDbContext.Create(databaseName);
        var organizationId = Guid.NewGuid();
        var role = Role.CreateCustom(organizationId, "Sales Rep");
        seedDb.Roles.Add(role);
        var untouchedRowId = Guid.NewGuid();
        seedDb.RolePermissions.Add(RolePermission.Create(untouchedRowId, role.Id, PermissionKeys.QuotationCreate, true));
        seedDb.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), role.Id, PermissionKeys.QuotationApprove, true));
        await seedDb.SaveChangesAsync();

        // Full desired state: keep QuotationCreate granted (untouched), revoke QuotationApprove,
        // newly grant QuotationEdit -- everything else in the catalog left at its (absent = false)
        // default.
        var grants = new Dictionary<string, bool>
        {
            [PermissionKeys.QuotationCreate] = true,
            [PermissionKeys.QuotationApprove] = false,
            [PermissionKeys.QuotationEdit] = true,
        };

        var db = TestAppDbContext.Create(databaseName);
        var handler = new UpdateRolePermissionsCommandHandler(db);
        await handler.Handle(new UpdateRolePermissionsCommand(organizationId, role.Id, grants), CancellationToken.None);

        var verifyDb = TestAppDbContext.Create(databaseName);
        var rows = verifyDb.RolePermissions.Where(rp => rp.RoleId == role.Id).ToList();

        Assert.True(rows.Single(rp => rp.Id == untouchedRowId).IsGranted);
        Assert.False(rows.Single(rp => rp.PermissionKey == PermissionKeys.QuotationApprove).IsGranted);
        Assert.True(rows.Single(rp => rp.PermissionKey == PermissionKeys.QuotationEdit).IsGranted);
        // No row is created for a key that was never granted and stays ungranted.
        Assert.DoesNotContain(rows, rp => rp.PermissionKey == PermissionKeys.QuotationView);
    }

    [Fact]
    public async Task UpdateRolePermissions_throws_conflict_for_a_system_role()
    {
        var db = TestAppDbContext.Create();
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        await db.SaveChangesAsync();

        var handler = new UpdateRolePermissionsCommandHandler(db);
        var grants = new Dictionary<string, bool> { [PermissionKeys.QuotationCreate] = true };

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new UpdateRolePermissionsCommand(Guid.NewGuid(), Role.MemberId, grants), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateMembershipRole_reassigns_role_for_an_accepted_membership()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        var newRole = Role.CreateCustom(organizationId, "Sales Rep");
        db.Roles.Add(newRole);
        var membership = OrganizationMembership.CreateAccepted(organizationId, Guid.NewGuid(), MembershipRole.Admin);
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        var handler = new UpdateMembershipRoleCommandHandler(db);
        await handler.Handle(new UpdateMembershipRoleCommand(organizationId, membership.Id, newRole.Id), CancellationToken.None);

        Assert.Equal(newRole.Id, db.OrganizationMemberships.Single(m => m.Id == membership.Id).RoleId);
    }

    [Fact]
    public async Task UpdateMembershipRole_throws_conflict_for_a_non_accepted_membership()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        var membership = OrganizationMembership.Invite(organizationId, userId: null, "pending@example.com", Role.MemberId, Guid.NewGuid());
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        var handler = new UpdateMembershipRoleCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new UpdateMembershipRoleCommand(organizationId, membership.Id, Role.MemberId), CancellationToken.None));
    }
}
