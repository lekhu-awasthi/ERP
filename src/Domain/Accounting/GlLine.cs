namespace ErpApp.Domain.Accounting;

/// <summary>
/// Child line of a posted GlJournalEntry -- own table, created only via GlJournalEntry.Post.
/// </summary>
public sealed class GlLine
{
    public Guid Id { get; private set; }
    public Guid GlJournalEntryId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }

    private GlLine()
    {
    }

    internal static GlLine Create(Guid glJournalEntryId, Guid accountId, decimal debit, decimal credit)
    {
        return new GlLine
        {
            Id = Guid.NewGuid(),
            GlJournalEntryId = glJournalEntryId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
        };
    }
}

/// <summary>
/// Pure input shape for IGlPostingRule&lt;TDocument&gt;.BuildLines -- not itself persisted; the
/// GlJournalEntry.Post factory turns a list of these into real, entry-linked GlLine rows.
/// </summary>
public sealed record GlLineInput(Guid AccountId, decimal Debit, decimal Credit);
