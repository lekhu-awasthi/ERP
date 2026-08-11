using ErpApp.Domain.Accounting;

namespace ErpApp.Domain.UnitTests.Accounting;

public class CashTransferTests
{
    [Fact]
    public void Create_starts_draft_with_placeholder_code_and_no_lines()
    {
        var fromAccountId = Guid.NewGuid();

        var cashTransfer = CashTransfer.Create(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), "Ref-1", fromAccountId);

        Assert.Equal(CashTransfer.DraftCode, cashTransfer.Code);
        Assert.Equal(CashTransferStatus.Draft, cashTransfer.Status);
        Assert.Equal(fromAccountId, cashTransfer.FromAccountId);
        Assert.Empty(cashTransfer.Lines);
    }

    [Fact]
    public void AddLine_rejects_zero_or_negative_amount()
    {
        var cashTransfer = CashTransfer.Create(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => cashTransfer.AddLine(Guid.NewGuid(), 0m));
        Assert.Throws<InvalidOperationException>(() => cashTransfer.AddLine(Guid.NewGuid(), -5m));
    }

    [Fact]
    public void Approve_assigns_code_and_flips_status_with_fan_out_lines()
    {
        var cashTransfer = CashTransfer.Create(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid());
        cashTransfer.AddLine(Guid.NewGuid(), 400m);
        cashTransfer.AddLine(Guid.NewGuid(), 600m);
        var approverId = Guid.NewGuid();

        cashTransfer.Approve(approverId, "CT-0001");

        Assert.Equal(CashTransferStatus.Approved, cashTransfer.Status);
        Assert.Equal("CT-0001", cashTransfer.Code);
        Assert.Equal(approverId, cashTransfer.ApprovedByUserId);
    }

    [Fact]
    public void Approve_throws_when_no_lines()
    {
        var cashTransfer = CashTransfer.Create(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => cashTransfer.Approve(Guid.NewGuid(), "CT-0001"));
    }

    [Fact]
    public void Approve_throws_when_a_destination_matches_the_from_account()
    {
        var fromAccountId = Guid.NewGuid();
        var cashTransfer = CashTransfer.Create(
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), null, fromAccountId);
        cashTransfer.AddLine(fromAccountId, 100m);

        Assert.Throws<InvalidOperationException>(() => cashTransfer.Approve(Guid.NewGuid(), "CT-0001"));
    }
}
