using ErpApp.Domain.Catalog;

namespace ErpApp.Domain.UnitTests.Catalog;

public class UnitOfMeasurementTests
{
    [Fact]
    public void Create_starts_active_with_given_name_and_short_name()
    {
        var organizationId = Guid.NewGuid();

        var unit = UnitOfMeasurement.Create(organizationId, "Kilogram", "kgs");

        Assert.Equal(organizationId, unit.OrganizationId);
        Assert.Equal("Kilogram", unit.Name);
        Assert.Equal("kgs", unit.ShortName);
        Assert.True(unit.IsActive);
    }

    [Fact]
    public void Update_replaces_name_short_name_and_active_flag()
    {
        var unit = UnitOfMeasurement.Create(Guid.NewGuid(), "Kilogram", "kgs");

        unit.Update("Kilograms", "kg", false);

        Assert.Equal("Kilograms", unit.Name);
        Assert.Equal("kg", unit.ShortName);
        Assert.False(unit.IsActive);
    }
}
