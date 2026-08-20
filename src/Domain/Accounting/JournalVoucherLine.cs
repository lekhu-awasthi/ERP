namespace ErpApp.Domain.Accounting;

/// <summary>
/// Child line of JournalVoucher -- own table, no aggregate-root behavior of its own, created only
/// via JournalVoucher.AddLine (same encapsulated-child-collection shape as
/// Catalog.ProductSecondaryUnit).
///
/// ContactId (docs/phase-17-status.md decision #2) tags a line as posting against a Contact's own
/// AR/AP control account -- optional, most JV lines (e.g. a straight expense accrual) tag no
/// Contact at all. A Contact-tagged, still-unallocated line is what lets a JournalVoucher become an
/// allocatable credit source on the Allocate Customer/Supplier Payment screens, alongside Payment
/// (see Payments.PaymentAllocation's generalized SourceType/SourceId).
/// </summary>
public sealed class JournalVoucherLine
{
    public Guid Id { get; private set; }
    public Guid JournalVoucherId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public Guid? ContactId { get; private set; }

    private JournalVoucherLine()
    {
    }

    internal static JournalVoucherLine Create(Guid journalVoucherId, Guid accountId, decimal debit, decimal credit, Guid? contactId)
    {
        return new JournalVoucherLine
        {
            Id = Guid.NewGuid(),
            JournalVoucherId = journalVoucherId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
            ContactId = contactId,
        };
    }
}
