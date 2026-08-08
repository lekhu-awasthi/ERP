using ErpApp.Application.Tenancy.Queries.CheckWorkspaceNameAvailability;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

public class CheckWorkspaceNameAvailabilityQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_available_for_unused_workspace_name()
    {
        var db = TestAppDbContext.Create();

        var result = await new CheckWorkspaceNameAvailabilityQueryHandler(db).Handle(
            new CheckWorkspaceNameAvailabilityQuery("acme-traders"), CancellationToken.None);

        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task Handle_returns_unavailable_case_insensitively_for_taken_workspace_name()
    {
        var db = TestAppDbContext.Create();
        db.Organizations.Add(Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var result = await new CheckWorkspaceNameAvailabilityQueryHandler(db).Handle(
            new CheckWorkspaceNameAvailabilityQuery("Acme-Traders"), CancellationToken.None);

        Assert.False(result.IsAvailable);
    }
}
