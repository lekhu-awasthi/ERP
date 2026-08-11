using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

public class CreateUnitOfMeasurementCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_unit()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var handler = new CreateUnitOfMeasurementCommandHandler(db);

        var result = await handler.Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Kilogram", "kgs"), CancellationToken.None);

        var unit = await db.UnitsOfMeasurement.SingleAsync(x => x.Id == result.Id);
        Assert.Equal("kgs", unit.ShortName);
    }

    [Fact]
    public async Task Handle_throws_conflict_when_name_already_used_in_organization()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.UnitsOfMeasurement.Add(UnitOfMeasurement.Create(organizationId, "Kilogram", "kgs"));
        await db.SaveChangesAsync();

        var handler = new CreateUnitOfMeasurementCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Kilogram", "kg"), CancellationToken.None));
    }
}
