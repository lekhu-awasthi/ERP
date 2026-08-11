using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.UnitTests.Configuration;

public class PaymentModeTests
{
    [Fact]
    public void Create_starts_active_with_given_name()
    {
        var paymentMode = PaymentMode.Create(Guid.NewGuid(), "Cash");

        Assert.Equal("Cash", paymentMode.Name);
        Assert.True(paymentMode.IsActive);
    }

    [Fact]
    public void Update_replaces_name_and_active_flag()
    {
        var paymentMode = PaymentMode.Create(Guid.NewGuid(), "Cash");

        paymentMode.Update("Bank Transfer", false);

        Assert.Equal("Bank Transfer", paymentMode.Name);
        Assert.False(paymentMode.IsActive);
    }
}
