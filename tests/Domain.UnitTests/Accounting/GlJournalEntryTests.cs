using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Accounting;

public class GlJournalEntryTests
{
    [Fact]
    public void Post_creates_a_balanced_entry_with_matching_lines()
    {
        var organizationId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();

        var entry = GlJournalEntry.Post(
            organizationId,
            DocumentType.JournalVoucher,
            sourceId,
            [new GlLineInput(accountA, 500m, 0m), new GlLineInput(accountB, 0m, 500m)]);

        Assert.Equal(organizationId, entry.OrganizationId);
        Assert.Equal(DocumentType.JournalVoucher, entry.SourceDocumentType);
        Assert.Equal(sourceId, entry.SourceDocumentId);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public void Post_throws_when_lines_are_empty()
    {
        Assert.Throws<InvalidOperationException>(() =>
            GlJournalEntry.Post(Guid.NewGuid(), DocumentType.JournalVoucher, Guid.NewGuid(), []));
    }

    [Fact]
    public void Post_throws_when_unbalanced()
    {
        Assert.Throws<InvalidOperationException>(() => GlJournalEntry.Post(
            Guid.NewGuid(),
            DocumentType.JournalVoucher,
            Guid.NewGuid(),
            [new GlLineInput(Guid.NewGuid(), 500m, 0m), new GlLineInput(Guid.NewGuid(), 0m, 400m)]));
    }
}
