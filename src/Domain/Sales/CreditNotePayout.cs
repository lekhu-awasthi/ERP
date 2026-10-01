using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 63 -- one way a refund at the till was paid back: a payment mode and the amount handed back
/// in it. The mirror of <see cref="InvoiceTender"/>, written once by <see cref="CreditNote.PayOut"/>.
///
/// <para>The mode's kind and account are <b>frozen</b> here for the tender's reason: the payout has
/// already posted to the account it named, and the session reader counts cash paid out by
/// <see cref="Kind"/>, so a mode re-pointed tomorrow cannot rewrite today's drawer.</para>
///
/// <para>What is <i>not</i> paid out is taken off what the customer owes, and is derived
/// (<see cref="CreditNote.ToAccountAmount"/>), never a row -- credit is not a tender on the sale either.</para>
/// </summary>
public sealed class CreditNotePayout
{
    public Guid Id { get; private set; }
    public Guid CreditNoteId { get; private set; }
    public Guid PaymentModeId { get; private set; }
    public PaymentModeKind Kind { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }

    private CreditNotePayout()
    {
    }

    internal static CreditNotePayout Create(
        Guid creditNoteId, Guid paymentModeId, PaymentModeKind kind, Guid accountId, decimal amount)
    {
        if (amount <= 0m)
        {
            throw new InvalidOperationException("A payout must be a positive amount.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException($"'{kind}' is not a payment mode kind.");
        }

        if (paymentModeId == Guid.Empty || accountId == Guid.Empty)
        {
            throw new InvalidOperationException("A payout names its payment mode and the account it posts from.");
        }

        if (decimal.Round(amount, Invoice.PosMoneyScale) != amount)
        {
            throw new InvalidOperationException($"A payout is whole paisa (at most {Invoice.PosMoneyScale} decimal places).");
        }

        return new CreditNotePayout
        {
            Id = Guid.NewGuid(),
            CreditNoteId = creditNoteId,
            PaymentModeId = paymentModeId,
            Kind = kind,
            AccountId = accountId,
            Amount = amount,
        };
    }
}
