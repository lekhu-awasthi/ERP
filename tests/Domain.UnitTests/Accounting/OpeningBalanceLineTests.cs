using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public class OpeningBalanceLineTests
{
    [Fact]
    public void Create_accepts_a_debit_only_line()
    {
        var line = OpeningBalanceLine.Create(Guid.NewGuid(), Guid.NewGuid(), 500m, 0m);

        Assert.Equal(500m, line.Debit);
        Assert.Equal(0m, line.Credit);
    }

    [Fact]
    public void Create_throws_when_both_debit_and_credit_are_zero()
    {
        Assert.Throws<InvalidOperationException>(() => OpeningBalanceLine.Create(Guid.NewGuid(), Guid.NewGuid(), 0m, 0m));
    }

    [Fact]
    public void Create_throws_when_both_debit_and_credit_are_nonzero()
    {
        Assert.Throws<InvalidOperationException>(() => OpeningBalanceLine.Create(Guid.NewGuid(), Guid.NewGuid(), 100m, 50m));
    }

    [Fact]
    public void Update_replaces_debit_and_credit()
    {
        var line = OpeningBalanceLine.Create(Guid.NewGuid(), Guid.NewGuid(), 500m, 0m);

        line.Update(0m, 300m);

        Assert.Equal(0m, line.Debit);
        Assert.Equal(300m, line.Credit);
    }
}
