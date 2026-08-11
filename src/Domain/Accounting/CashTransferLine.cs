namespace ErpApp.Domain.Accounting;

/// <summary>
/// Child "destination" line of CashTransfer -- one row per (ToAccountId, Amount) fan-out target,
/// created only via CashTransfer.AddLine.
/// </summary>
public sealed class CashTransferLine
{
    public Guid Id { get; private set; }
    public Guid CashTransferId { get; private set; }
    public Guid ToAccountId { get; private set; }
    public decimal Amount { get; private set; }

    private CashTransferLine()
    {
    }

    internal static CashTransferLine Create(Guid cashTransferId, Guid toAccountId, decimal amount)
    {
        return new CashTransferLine
        {
            Id = Guid.NewGuid(),
            CashTransferId = cashTransferId,
            ToAccountId = toAccountId,
            Amount = amount,
        };
    }
}
