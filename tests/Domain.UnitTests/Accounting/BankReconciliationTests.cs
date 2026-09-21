using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public sealed class BankReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 15, 19, 0, TimeSpan.FromHours(5.75));

    [Fact]
    public void A_matched_pair_reconciles()
    {
        var reconciliation = Create(Deposit(791m), 1, Deposit(791m), 2);

        Assert.NotEqual(Guid.Empty, reconciliation.Id);
        Assert.Equal(Now, reconciliation.ReconciledAt);
        Assert.NotEqual(Guid.Empty, reconciliation.ReconciledByUserId);
    }

    /// <summary>
    /// <b>The invariant, and it is the reference product's own.</b> Its endpoint answered
    /// <c>400 "transactions cannot be reconciled"</c> to 678 against 113, probed live on
    /// 2026-09-21 -- so this is a rule read off the server, not one inferred from a disabled button.
    /// </summary>
    [Fact]
    public void Unequal_totals_are_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Create(Deposit(678m), 1, Deposit(113m), 1));

        Assert.Contains("total the same amount", ex.Message);
        Assert.Contains("678", ex.Message);
        Assert.Contains("113", ex.Message);
    }

    /// <summary>
    /// A selection that nets to nothing is legal -- the reference product's gate reads
    /// <c>!(bank != tigg || (bank == 0 &amp;&amp; bankRows == 0) || (tigg == 0 &amp;&amp; tiggRows == 0))</c>,
    /// i.e. zero is only refused when it is the empty sum. This is why the counts are separate
    /// arguments from the totals.
    /// </summary>
    [Fact]
    public void A_zero_total_with_rows_on_both_sides_is_allowed()
    {
        var netting = BankMovementTotal.OfStatementLines(
            [StatementAmount.Deposit(100m), StatementAmount.Withdrawal(100m)]);

        var reconciliation = Create(netting, 2, netting, 2);

        Assert.NotEqual(Guid.Empty, reconciliation.Id);
    }

    [Fact]
    public void An_empty_side_is_refused_even_though_the_totals_agree()
    {
        var bankOnly = Assert.Throws<InvalidOperationException>(
            () => Create(BankMovementTotal.Zero, 0, BankMovementTotal.Zero, 1));
        Assert.Contains("bank statement line", bankOnly.Message);

        var bookOnly = Assert.Throws<InvalidOperationException>(
            () => Create(BankMovementTotal.Zero, 1, BankMovementTotal.Zero, 0));
        Assert.Contains("book transaction", bookOnly.Message);
    }

    [Theory]
    [InlineData("organizationId")]
    [InlineData("bankAccountId")]
    [InlineData("reconciledByUserId")]
    public void An_empty_identifier_is_refused(string argument)
    {
        var ex = Assert.Throws<ArgumentException>(() => BankReconciliation.Create(
            argument == "organizationId" ? Guid.Empty : Guid.NewGuid(),
            argument == "bankAccountId" ? Guid.Empty : Guid.NewGuid(),
            Deposit(1m), 1, Deposit(1m), 1,
            argument == "reconciledByUserId" ? Guid.Empty : Guid.NewGuid(),
            Now));

        Assert.Equal(argument, ex.ParamName);
    }

    /// <summary>
    /// <b>The aggregate's central claim, asserted as a shape.</b> A reconciliation posts nothing and
    /// is not a document: no amount of its own (which is the kickoff's rule -- a stored total could
    /// disagree with the sum of what it joins), no code, no lifecycle. The premise half is asserted
    /// too, so a rename cannot make this vacuous (phase 54).
    /// </summary>
    [Fact]
    public void A_reconciliation_carries_only_who_and_when()
    {
        var properties = typeof(BankReconciliation)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Amount", properties);
        Assert.DoesNotContain("Total", properties);
        Assert.DoesNotContain("MatchedAmount", properties);
        Assert.DoesNotContain("Code", properties);
        Assert.DoesNotContain("Status", properties);
        Assert.DoesNotContain("ApprovedAt", properties);
        Assert.DoesNotContain("VoidedAt", properties);

        Assert.Contains("ReconciledAt", properties);
        Assert.Contains("ReconciledByUserId", properties);
        Assert.Contains("BankAccountId", properties);
    }

    private static BankMovementTotal Deposit(decimal amount) =>
        BankMovementTotal.OfStatementLines([StatementAmount.Deposit(amount)]);

    private static BankReconciliation Create(
        BankMovementTotal statementTotal, int statementCount,
        BankMovementTotal bookTotal, int bookCount) =>
        BankReconciliation.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            statementTotal, statementCount, bookTotal, bookCount,
            Guid.NewGuid(), Now);
}
