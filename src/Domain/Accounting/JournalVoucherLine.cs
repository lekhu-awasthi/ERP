namespace ErpApp.Domain.Accounting;

/// <summary>
/// Child line of JournalVoucher -- own table, no aggregate-root behavior of its own, created only
/// via JournalVoucher.AddLine (same encapsulated-child-collection shape as
/// Catalog.ProductSecondaryUnit).
/// </summary>
public sealed class JournalVoucherLine
{
    public Guid Id { get; private set; }
    public Guid JournalVoucherId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }

    private JournalVoucherLine()
    {
    }

    internal static JournalVoucherLine Create(Guid journalVoucherId, Guid accountId, decimal debit, decimal credit)
    {
        return new JournalVoucherLine
        {
            Id = Guid.NewGuid(),
            JournalVoucherId = journalVoucherId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
        };
    }
}
