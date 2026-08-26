using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class CostTermTests
{
    [Fact]
    public void Create_starts_active_in_the_given_category()
    {
        var costTerm = CostTerm.Create(Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost);

        Assert.Equal("Freight", costTerm.Name);
        Assert.Equal(CostTermCategory.AdditionalCost, costTerm.Category);
        Assert.True(costTerm.IsActive);
    }

    [Fact]
    public void Update_replaces_name_category_and_active_flag()
    {
        var costTerm = CostTerm.Create(Guid.NewGuid(), "Freight", CostTermCategory.AdditionalCost);

        costTerm.Update("Machine Hours", CostTermCategory.ProductionCost, false);

        Assert.Equal("Machine Hours", costTerm.Name);
        Assert.Equal(CostTermCategory.ProductionCost, costTerm.Category);
        Assert.False(costTerm.IsActive);
    }
}
