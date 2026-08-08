using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Tenancy;

public class CreateOrganizationCommandHandlerTests
{
    private static CreateOrganizationCommand ValidCommand() => new(
        Name: "Acme Traders",
        Industry: "Retail",
        Address: "Kathmandu",
        AccountingStartDate: new DateOnly(2026, 1, 1),
        IsVatRegistered: true,
        WorkspaceName: "Acme-Traders",
        Email: null,
        Phone: null,
        PanNumber: null,
        Website: null,
        TrackInventory: true,
        MultipleLocations: false,
        MultipleWarehouses: false,
        MultiCurrency: false,
        Manufacturing: false,
        PosRetail: false,
        PosRestaurant: false);

    [Fact]
    public async Task Handle_creates_organization_with_seeded_settings_subscription_and_admin_membership()
    {
        var db = TestAppDbContext.Create();
        var creatorId = Guid.NewGuid();
        var handler = new CreateOrganizationCommandHandler(db, new FakeCurrentUserService(creatorId));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        var organization = await db.Organizations.SingleAsync(o => o.Id == result.OrganizationId);
        Assert.Equal("acme-traders", organization.WorkspaceName);
        Assert.Equal(creatorId, organization.CreatedByUserId);

        var settings = await db.TenantSettings.SingleAsync(s => s.OrganizationId == organization.Id);
        Assert.NotEqual(Guid.Empty, settings.Id);

        var subscription = await db.TenantSubscriptions.SingleAsync(s => s.OrganizationId == organization.Id);
        Assert.True(subscription.TrackInventoryEnabled);
        Assert.False(subscription.MultiCurrencyEnabled);

        var membership = await db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == organization.Id);
        Assert.Equal(creatorId, membership.UserId);
        Assert.Equal(MembershipRole.Admin, membership.Role);
        Assert.Equal(MembershipStatus.Accepted, membership.Status);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_workspace_name_already_taken()
    {
        var db = TestAppDbContext.Create();
        db.Organizations.Add(Organization.Create(
            "Existing Org", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var handler = new CreateOrganizationCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(ValidCommand(), CancellationToken.None));
    }
}
