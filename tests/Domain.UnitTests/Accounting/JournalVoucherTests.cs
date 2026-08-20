using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public class JournalVoucherTests
{
    [Fact]
    public void Create_starts_draft_with_placeholder_code_and_no_lines()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "Ref-1");

        Assert.Equal(JournalVoucher.DraftCode, journalVoucher.Code);
        Assert.Equal(JournalVoucherStatus.Draft, journalVoucher.Status);
        Assert.Empty(journalVoucher.Lines);
    }

    [Fact]
    public void AddLine_appends_a_valid_debit_or_credit_line()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        var accountId = Guid.NewGuid();

        journalVoucher.AddLine(accountId, 100m, 0m);

        Assert.Single(journalVoucher.Lines);
        Assert.Equal(accountId, journalVoucher.Lines[0].AccountId);
        Assert.Equal(100m, journalVoucher.Lines[0].Debit);
        Assert.Equal(0m, journalVoucher.Lines[0].Credit);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(-10, 0)]
    public void AddLine_rejects_a_line_without_exactly_one_nonzero_side(decimal debit, decimal credit)
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);

        Assert.Throws<InvalidOperationException>(() => journalVoucher.AddLine(Guid.NewGuid(), debit, credit));
    }

    [Fact]
    public void AddLine_carries_an_optional_contact_id()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        var contactId = Guid.NewGuid();

        journalVoucher.AddLine(Guid.NewGuid(), 0m, 100m, contactId);
        journalVoucher.AddLine(Guid.NewGuid(), 100m, 0m);

        Assert.Equal(contactId, journalVoucher.Lines[0].ContactId);
        Assert.Null(journalVoucher.Lines[1].ContactId);
    }

    [Fact]
    public void ClearLines_removes_every_line()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        journalVoucher.AddLine(Guid.NewGuid(), 100m, 0m);
        journalVoucher.AddLine(Guid.NewGuid(), 0m, 100m);

        journalVoucher.ClearLines();

        Assert.Empty(journalVoucher.Lines);
    }

    [Fact]
    public void Approve_assigns_code_and_flips_status_when_balanced_with_two_or_more_lines()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        journalVoucher.AddLine(Guid.NewGuid(), 100m, 0m);
        journalVoucher.AddLine(Guid.NewGuid(), 0m, 100m);
        var approverId = Guid.NewGuid();

        journalVoucher.Approve(approverId, "JV-0001");

        Assert.Equal(JournalVoucherStatus.Approved, journalVoucher.Status);
        Assert.Equal("JV-0001", journalVoucher.Code);
        Assert.Equal(approverId, journalVoucher.ApprovedByUserId);
        Assert.NotNull(journalVoucher.ApprovedAt);
    }

    [Fact]
    public void Approve_throws_when_fewer_than_two_lines()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        journalVoucher.AddLine(Guid.NewGuid(), 100m, 0m);

        Assert.Throws<InvalidOperationException>(() => journalVoucher.Approve(Guid.NewGuid(), "JV-0001"));
    }

    [Fact]
    public void Approve_throws_when_unbalanced()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        journalVoucher.AddLine(Guid.NewGuid(), 100m, 0m);
        journalVoucher.AddLine(Guid.NewGuid(), 0m, 50m);

        Assert.Throws<InvalidOperationException>(() => journalVoucher.Approve(Guid.NewGuid(), "JV-0001"));
    }

    [Fact]
    public void Mutation_throws_once_approved()
    {
        var journalVoucher = JournalVoucher.Create(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null);
        journalVoucher.AddLine(Guid.NewGuid(), 100m, 0m);
        journalVoucher.AddLine(Guid.NewGuid(), 0m, 100m);
        journalVoucher.Approve(Guid.NewGuid(), "JV-0001");

        Assert.Throws<InvalidOperationException>(() => journalVoucher.AddLine(Guid.NewGuid(), 10m, 0m));
        Assert.Throws<InvalidOperationException>(() => journalVoucher.ClearLines());
        Assert.Throws<InvalidOperationException>(() => journalVoucher.UpdateHeader(DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.Throws<InvalidOperationException>(() => journalVoucher.Approve(Guid.NewGuid(), "JV-0002"));
    }
}
