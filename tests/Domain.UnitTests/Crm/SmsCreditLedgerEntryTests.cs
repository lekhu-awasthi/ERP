using ErpApp.Domain.Crm;

namespace ErpApp.Domain.UnitTests.Crm;

public class SmsCreditLedgerEntryTests
{
    [Fact]
    public void CreateManualAdjustment_sets_positive_change_amount_and_reason()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var entry = SmsCreditLedgerEntry.CreateManualAdjustment(organizationId, 500, "Initial credit purchase", userId);

        Assert.Equal(organizationId, entry.OrganizationId);
        Assert.Equal(SmsCreditLedgerEntryType.ManualAdjustment, entry.Type);
        Assert.Equal(500, entry.ChangeAmount);
        Assert.Equal("Initial credit purchase", entry.Reason);
        Assert.Null(entry.RelatedSmsBatchId);
        Assert.Equal(userId, entry.CreatedByUserId);
    }

    [Fact]
    public void CreateManualAdjustment_allows_a_negative_correction()
    {
        var entry = SmsCreditLedgerEntry.CreateManualAdjustment(Guid.NewGuid(), -100, "Correction", Guid.NewGuid());

        Assert.Equal(-100, entry.ChangeAmount);
    }

    [Fact]
    public void CreateSendDebit_negates_credits_used_and_links_the_batch()
    {
        var organizationId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var entry = SmsCreditLedgerEntry.CreateSendDebit(organizationId, creditsUsed: 12, batchId, userId);

        Assert.Equal(SmsCreditLedgerEntryType.Send, entry.Type);
        Assert.Equal(-12, entry.ChangeAmount);
        Assert.Equal(batchId, entry.RelatedSmsBatchId);
        Assert.Null(entry.Reason);
    }
}
