using ErpApp.Domain.Inventory;

namespace ErpApp.Domain.UnitTests.Inventory;

public class OpeningStockLineTests
{
    [Fact]
    public void Create_starts_with_given_quantity_and_rate()
    {
        var line = OpeningStockLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 25m);

        Assert.Equal(10m, line.Quantity);
        Assert.Equal(25m, line.Rate);
    }

    [Fact]
    public void Create_throws_when_quantity_is_not_positive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OpeningStockLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m, 25m));
    }

    [Fact]
    public void Update_replaces_quantity_and_rate()
    {
        var line = OpeningStockLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 25m);

        line.Update(20m, 30m);

        Assert.Equal(20m, line.Quantity);
        Assert.Equal(30m, line.Rate);
    }
}
