using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Configuration.Commands.CreateAlertDefinition;
using ErpApp.Application.Configuration.Commands.SetAlertDefinitionActive;
using ErpApp.Application.Configuration.Commands.UpdateAlertDefinition;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Configuration;

public class AlertDefinitionCommandHandlerTests
{
    [Fact]
    public async Task Create_stores_the_alert_active_and_attributed_to_the_caller()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(userId))
            .Handle(NewCommand(organizationId), CancellationToken.None);

        var alert = await db.AlertDefinitions.SingleAsync(x => x.Id == result.Id);
        Assert.True(alert.IsActive);
        Assert.Equal(userId, alert.CreatedByUserId);
        Assert.Equal(new TimeOnly(19, 57), alert.ScheduleTime);
        Assert.Equal(["ops@example.test"], alert.RecipientAddresses);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_within_the_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        await handler.Handle(NewCommand(organizationId), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(NewCommand(organizationId), CancellationToken.None));
    }

    [Fact]
    public async Task Create_allows_the_same_name_in_a_different_organization()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        await handler.Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);

        var result = await handler.Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Update_replaces_the_editable_fields()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var created = await new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(NewCommand(organizationId), CancellationToken.None);

        await new UpdateAlertDefinitionCommandHandler(db).Handle(
            new UpdateAlertDefinitionCommand(
                organizationId, created.Id, "Renamed", AlertMedium.Email, AlertType.CrmReport,
                "a@example.test, b@example.test", AlertScheduleFrequency.Daily, new TimeOnly(6, 0), false),
            CancellationToken.None);

        var alert = await db.AlertDefinitions.SingleAsync(x => x.Id == created.Id);
        Assert.Equal("Renamed", alert.Name);
        Assert.Equal(AlertType.CrmReport, alert.AlertType);
        Assert.Equal(2, alert.RecipientAddresses.Count);
        Assert.False(alert.IsActive);
    }

    /// <summary>Tenant isolation: a caller in another organization must get NotFound, never a
    /// silent cross-tenant edit. (The permission gate itself is AuthorizationBehavior's job; this is
    /// the handler's own OrganizationId filter, which CLAUDE.md requires every handler to carry.)</summary>
    [Fact]
    public async Task Update_cannot_reach_another_organizations_alert()
    {
        var db = TestAppDbContext.Create();
        var owner = Guid.NewGuid();
        var created = await new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(NewCommand(owner), CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateAlertDefinitionCommandHandler(db).Handle(
                new UpdateAlertDefinitionCommand(
                    Guid.NewGuid(), created.Id, "Hijacked", AlertMedium.Email,
                    AlertType.DailyTransactionSummary, "x@example.test",
                    AlertScheduleFrequency.Daily, new TimeOnly(7, 0), true),
                CancellationToken.None));
    }

    [Fact]
    public async Task SetActive_toggles_only_the_active_flag()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var created = await new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(NewCommand(organizationId), CancellationToken.None);

        await new SetAlertDefinitionActiveCommandHandler(db).Handle(
            new SetAlertDefinitionActiveCommand(organizationId, created.Id, false), CancellationToken.None);

        var alert = await db.AlertDefinitions.SingleAsync(x => x.Id == created.Id);
        Assert.False(alert.IsActive);
        Assert.Equal("Daily summary", alert.Name);
    }

    [Fact]
    public async Task SetActive_cannot_reach_another_organizations_alert()
    {
        var db = TestAppDbContext.Create();
        var created = await new CreateAlertDefinitionCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SetAlertDefinitionActiveCommandHandler(db).Handle(
                new SetAlertDefinitionActiveCommand(Guid.NewGuid(), created.Id, false), CancellationToken.None));
    }

    private static CreateAlertDefinitionCommand NewCommand(Guid organizationId) =>
        new(organizationId, "Daily summary", AlertMedium.Email, AlertType.DailyTransactionSummary,
            "ops@example.test", AlertScheduleFrequency.Daily, new TimeOnly(19, 57));
}
