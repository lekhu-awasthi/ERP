using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public class BankStatementLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_a_line_against_an_account()
    {
        var organizationId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var line = BankStatementLine.Create(
            organizationId, accountId, new DateOnly(2026, 9, 1), "Salary credit",
            StatementAmount.Deposit(1500m), jobId, Now);

        Assert.Equal(organizationId, line.OrganizationId);
        Assert.Equal(accountId, line.BankAccountId);
        Assert.Equal(new DateOnly(2026, 9, 1), line.Date);
        Assert.Equal("Salary credit", line.Description);
        Assert.Equal(1500m, line.Amount.Signed);
        Assert.Equal(jobId, line.ImportJobId);
        Assert.Equal(Now, line.CreatedAt);
    }

    /// <summary>The reference product accepts a row with no description (probed live), and a blank
    /// narration is ordinary on a fee or an interest line. Blank and whitespace both normalise to
    /// null so the list does not show a row that looks like it has a description and does not.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_description_is_stored_as_null(string? description)
    {
        var line = NewLine(description: description);

        Assert.Null(line.Description);
    }

    [Fact]
    public void A_description_is_trimmed()
    {
        Assert.Equal("ATM withdrawal", NewLine(description: "  ATM withdrawal  ").Description);
    }

    [Fact]
    public void A_line_must_belong_to_an_organization_and_an_account()
    {
        Assert.Throws<ArgumentException>(() => BankStatementLine.Create(
            Guid.Empty, Guid.NewGuid(), new DateOnly(2026, 9, 1), null,
            StatementAmount.Deposit(1m), null, Now));

        Assert.Throws<ArgumentException>(() => BankStatementLine.Create(
            Guid.NewGuid(), Guid.Empty, new DateOnly(2026, 9, 1), null,
            StatementAmount.Deposit(1m), null, Now));
    }

    /// <summary>
    /// <b>The invariant the aggregate's doc comment states, asserted as a shape rather than as
    /// prose.</b> A statement line has no status, no document number and no approval: the next
    /// phase to touch this will want to give it a lifecycle, and the doc comment says why not.
    /// This fails the moment one is added, which is the point -- adding one is a decision that
    /// should have to delete a test and read its reason (phase 45's rule about a refusal asserted
    /// in the negative).
    /// </summary>
    [Fact]
    public void A_statement_line_has_no_lifecycle_of_its_own()
    {
        var properties = typeof(BankStatementLine)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Status", properties);
        Assert.DoesNotContain("Code", properties);
        Assert.DoesNotContain("DocumentCode", properties);
        Assert.DoesNotContain("ApprovedAt", properties);
        Assert.DoesNotContain("VoidedAt", properties);

        // The premise half, so a rename cannot make the assertions above vacuously true
        // (phase 54's UnitlessOutputHeaders lesson).
        Assert.Contains("Amount", properties);
        Assert.Contains("Date", properties);
        Assert.Contains("BankAccountId", properties);
    }

    /// <summary>
    /// <b>The hole a private constructor cannot close, and the guard that found it.</b> C# hands
    /// every caller <c>default(StatementAmount)</c> however inaccessible the constructor is, and a
    /// default one is a zero. <c>SortSweepGuardTests</c> built exactly that by reflection and the
    /// failure surfaced on the way back <i>out</i> of the database, in the EF value converter --
    /// i.e. as a list that would not load rather than as a bad write. The aggregate checks it so
    /// the failure lands on the write, where it can name the argument.
    /// </summary>
    [Fact]
    public void A_default_constructed_amount_is_refused_at_the_write()
    {
        var ex = Assert.Throws<ArgumentException>(() => BankStatementLine.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), null,
            default, null, Now));

        Assert.Equal("amount", ex.ParamName);
    }

    private static BankStatementLine NewLine(string? description) => BankStatementLine.Create(
        Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), description,
        StatementAmount.Withdrawal(40m), null, Now);
}
