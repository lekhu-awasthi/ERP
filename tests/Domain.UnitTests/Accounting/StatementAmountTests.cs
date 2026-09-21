using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

/// <summary>
/// Phase 55 -- the value type that keeps a statement's direction out of a bare <c>decimal</c>.
/// </summary>
public class StatementAmountTests
{
    [Fact]
    public void A_deposit_is_positive_and_a_withdrawal_is_negative()
    {
        Assert.Equal(1500m, StatementAmount.Deposit(1500m).Signed);
        Assert.Equal(-250.75m, StatementAmount.Withdrawal(250.75m).Signed);
    }

    /// <summary>
    /// A withdrawal is built from the <i>positive</i> figure in the Withdrawal column, so a caller
    /// never negates anything itself. Passing a pre-negated value is the mistake the type exists
    /// to catch, and it is caught.
    /// </summary>
    [Fact]
    public void A_withdrawal_takes_the_magnitude_the_column_shows_not_a_negated_one()
    {
        var withdrawal = StatementAmount.Withdrawal(250.75m);

        Assert.Equal(250.75m, withdrawal.WithdrawalAmount);
        Assert.Equal(0m, withdrawal.DepositAmount);
        Assert.False(withdrawal.IsDeposit);

        Assert.Throws<ArgumentOutOfRangeException>(() => StatementAmount.Withdrawal(-250.75m));
    }

    /// <summary>The reference product's own rule, read live: <i>"both deposit and withdrawal cannot
    /// be zero"</i>. A zero-value line could never match anything in phase 56.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_deposit_must_be_a_positive_amount(decimal magnitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementAmount.Deposit(magnitude));
    }

    [Fact]
    public void Zero_is_not_a_statement_amount_even_through_the_persistence_factory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementAmount.FromSigned(0m));
    }

    /// <summary>
    /// The round trip the EF value converter performs. Asserted because the converter is the only
    /// place a raw signed decimal is allowed to become an amount, so if these two disagree every
    /// stored line reads back with the wrong direction.
    /// </summary>
    [Theory]
    [InlineData(1500)]
    [InlineData(-250.75)]
    [InlineData(0.01)]
    public void Signed_round_trips_through_FromSigned(decimal signed)
    {
        Assert.Equal(signed, StatementAmount.FromSigned(signed).Signed);
    }

    /// <summary>
    /// <c>decimal</c> keeps a signed zero, and <c>-0m == 0m</c> so no equality assertion can see
    /// one -- it surfaces as <c>-0.00</c> once a spreadsheet cell casts it to <c>double</c>
    /// (phase 26c). The refusal of a zero magnitude is what makes it unreachable here, and this
    /// checks the sign bit itself rather than the value.
    /// </summary>
    [Fact]
    public void No_amount_can_carry_a_negative_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementAmount.Withdrawal(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementAmount.FromSigned(-0m));
    }

    /// <summary>
    /// Two columns that must not contradict each other are the shape this type exists to avoid, so
    /// the display accessors are derived from the one stored value and exactly one is ever
    /// non-zero. Phase 51's ProductBatch argument, at the scale of a single row.
    /// </summary>
    [Theory]
    [InlineData(1500, 1500, 0)]
    [InlineData(-250.75, 0, 250.75)]
    public void Exactly_one_display_column_is_ever_non_zero(decimal signed, decimal deposit, decimal withdrawal)
    {
        var amount = StatementAmount.FromSigned(signed);

        Assert.Equal(deposit, amount.DepositAmount);
        Assert.Equal(withdrawal, amount.WithdrawalAmount);
    }
}
