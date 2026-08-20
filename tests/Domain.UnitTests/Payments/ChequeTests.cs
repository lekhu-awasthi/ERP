using ErpApp.Domain.Payments;

namespace ErpApp.Domain.UnitTests.Payments;

public class ChequeTests
{
    private static Cheque CreateCheque(decimal amount = 1000m) =>
        Cheque.Create(
            Guid.NewGuid(), Guid.NewGuid(), PaymentDirection.Received, Guid.NewGuid(), "46657575",
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow), amount);

    [Fact]
    public void Create_starts_pending()
    {
        var cheque = CreateCheque();

        Assert.Equal(ChequeStatus.Pending, cheque.Status);
    }

    [Theory]
    [InlineData(ChequeStatus.Deposited)]
    [InlineData(ChequeStatus.Cleared)]
    [InlineData(ChequeStatus.Bounced)]
    [InlineData(ChequeStatus.Cancelled)]
    public void TransitionStatus_allows_every_forward_move_from_pending(ChequeStatus target)
    {
        var cheque = CreateCheque();

        cheque.TransitionStatus(target);

        Assert.Equal(target, cheque.Status);
    }

    [Theory]
    [InlineData(ChequeStatus.Cleared)]
    [InlineData(ChequeStatus.Bounced)]
    [InlineData(ChequeStatus.Cancelled)]
    public void TransitionStatus_allows_deposited_to_terminal_states(ChequeStatus target)
    {
        var cheque = CreateCheque();
        cheque.TransitionStatus(ChequeStatus.Deposited);

        cheque.TransitionStatus(target);

        Assert.Equal(target, cheque.Status);
    }

    [Theory]
    [InlineData(ChequeStatus.Cleared)]
    [InlineData(ChequeStatus.Bounced)]
    [InlineData(ChequeStatus.Cancelled)]
    public void TransitionStatus_rejects_any_move_out_of_a_terminal_state(ChequeStatus terminal)
    {
        var cheque = CreateCheque();
        cheque.TransitionStatus(terminal);

        Assert.Throws<InvalidOperationException>(() => cheque.TransitionStatus(ChequeStatus.Deposited));
    }

    [Fact]
    public void TransitionStatus_rejects_moving_backward_to_pending()
    {
        var cheque = CreateCheque();
        cheque.TransitionStatus(ChequeStatus.Deposited);

        Assert.Throws<InvalidOperationException>(() => cheque.TransitionStatus(ChequeStatus.Pending));
    }

    [Fact]
    public void UpdateDetails_throws_once_no_longer_pending()
    {
        var cheque = CreateCheque();
        cheque.TransitionStatus(ChequeStatus.Deposited);

        Assert.Throws<InvalidOperationException>(() =>
            cheque.UpdateDetails(Guid.NewGuid(), "99999", DateOnly.FromDateTime(DateTime.UtcNow), null, 500m));
    }
}
