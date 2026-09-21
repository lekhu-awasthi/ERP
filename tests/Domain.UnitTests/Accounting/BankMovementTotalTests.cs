using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Accounting;

/// <summary>
/// Phase 56. The type exists for exactly one comparison -- the bank's side against this tenant's --
/// so these tests are about the two sign conventions meeting correctly, not about arithmetic.
/// </summary>
public sealed class BankMovementTotalTests
{
    [Fact]
    public void A_statement_total_sums_deposits_and_withdrawals_signed()
    {
        var total = BankMovementTotal.OfStatementLines(
        [
            StatementAmount.Deposit(678m),
            StatementAmount.Deposit(113m),
            StatementAmount.Withdrawal(91m),
        ]);

        Assert.Equal(700m, total.Signed);
    }

    /// <summary>
    /// <b>The conversion the type exists for.</b> A cash-and-bank account is an asset, so a debit
    /// to it is money arriving -- which is the statement's convention exactly. If this is ever
    /// inverted, every reconciliation in the product silently matches the wrong direction and the
    /// sums still balance in half the cases, which is why it is asserted directly rather than only
    /// through a handler.
    /// </summary>
    [Fact]
    public void A_debit_to_the_bank_account_is_money_in_and_a_credit_is_money_out()
    {
        var entry = GlJournalEntry.Post(
            Guid.NewGuid(), DocumentType.Invoice, Guid.NewGuid(),
            [new GlLineInput(BankAccount, 678m, 0m), new GlLineInput(Receivable, 0m, 678m)]);

        var bankLine = entry.Lines.Single(x => x.AccountId == BankAccount);
        var contraLine = entry.Lines.Single(x => x.AccountId == Receivable);

        Assert.Equal(678m, BankMovementTotal.OfGlLine(bankLine).Signed);
        Assert.Equal(-678m, BankMovementTotal.OfGlLine(contraLine).Signed);
    }

    /// <summary>
    /// The two sides of the live 1:2 match probed on the reference product: one 791 deposit against
    /// two receipts of 678 and 113. This is the equality <c>BankReconciliation.Create</c> enforces.
    /// </summary>
    [Fact]
    public void The_two_sides_of_a_real_match_compare_equal()
    {
        var entry = GlJournalEntry.Post(
            Guid.NewGuid(), DocumentType.Invoice, Guid.NewGuid(),
            [
                new GlLineInput(BankAccount, 678m, 0m),
                new GlLineInput(BankAccount, 113m, 0m),
                new GlLineInput(Receivable, 0m, 791m),
            ]);

        var book = BankMovementTotal.OfGlLines(entry.Lines.Where(x => x.AccountId == BankAccount));
        var bank = BankMovementTotal.OfStatementLines([StatementAmount.Deposit(791m)]);

        Assert.Equal(bank, book);
    }

    /// <summary>
    /// Zero is legal here and illegal on <see cref="StatementAmount"/>, and the difference is the
    /// reason the two are separate types: the reference product's own gate permits reconciling a
    /// selection that nets off, so long as rows are selected on both sides.
    /// </summary>
    [Fact]
    public void A_total_may_be_zero_where_a_line_amount_may_not()
    {
        var netting = BankMovementTotal.OfStatementLines(
            [StatementAmount.Deposit(100m), StatementAmount.Withdrawal(100m)]);

        Assert.Equal(BankMovementTotal.Zero, netting);
        Assert.Equal(BankMovementTotal.Zero, BankMovementTotal.OfStatementLines([]));
        Assert.Equal(default, BankMovementTotal.Zero);

        // ...and the half that makes the sentence above mean something.
        Assert.Throws<ArgumentOutOfRangeException>(() => StatementAmount.Deposit(0m));
    }

    [Fact]
    public void Subtraction_is_the_reports_difference_row()
    {
        var bank = BankMovementTotal.OfStatementLines([StatementAmount.Deposit(1582m)]);
        var book = BankMovementTotal.OfStatementLines([StatementAmount.Deposit(791m)]);

        Assert.Equal(791m, (bank - book).Signed);
        Assert.Equal(-791m, (book - bank).Signed);
    }

    /// <summary>
    /// Phase 52's and phase 55's rule in a third place: no implicit conversion, no public
    /// constructor, so nothing can hand this a decimal signed by a different convention.
    /// </summary>
    [Fact]
    public void There_is_no_way_to_build_one_from_a_bare_decimal()
    {
        Assert.Empty(typeof(BankMovementTotal).GetConstructors());

        var conversions = typeof(BankMovementTotal)
            .GetMethods()
            .Where(m => m.Name is "op_Implicit" or "op_Explicit");

        Assert.Empty(conversions);
    }

    private static readonly Guid BankAccount = Guid.NewGuid();
    private static readonly Guid Receivable = Guid.NewGuid();
}
