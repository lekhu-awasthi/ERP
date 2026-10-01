using ErpApp.Domain.Configuration;

namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 61 -- one way a till sale was paid: a payment mode and the amount handed over in it
/// (phase 59 Decision D). Child of <see cref="Invoice"/>, written once by <see cref="Invoice.Settle"/>.
///
/// <para><b>The mode's kind and account are frozen here</b>, not read back from the
/// <see cref="PaymentMode"/>. Both are editable on the mode after the sale, and the tender has
/// already posted to the account it named -- phase 52's rule for a line's unit factor, applied to a
/// settlement. The session reader counts cash by <see cref="Kind"/>, so a mode re-kinded tomorrow
/// cannot rewrite yesterday's drawer.</para>
///
/// <para><b>Credit is not a tender.</b> The vendor posts <c>{type: "Credit", amount}</c> beside its
/// real tenders. Here the unsettled remainder is <see cref="Invoice.CreditAmount"/>, derived: a
/// receivable is what is left when nothing was handed over, and storing it as a row would be a
/// second figure for one fact.</para>
/// </summary>
public sealed class InvoiceTender
{
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid PaymentModeId { get; private set; }
    public PaymentModeKind Kind { get; private set; }
    public Guid AccountId { get; private set; }

    /// <summary>What was handed over in this mode. A cash tender is the cash <i>received</i>, before
    /// any change is given back -- the change is on the invoice, because it always comes out of the
    /// drawer whatever was tendered (see <see cref="Invoice.ChangeAmount"/>).</summary>
    public decimal Amount { get; private set; }

    private InvoiceTender()
    {
    }

    internal static InvoiceTender Create(
        Guid invoiceId, Guid paymentModeId, PaymentModeKind kind, Guid accountId, decimal amount)
    {
        if (amount <= 0m)
        {
            throw new InvalidOperationException("A tender must be a positive amount.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException($"'{kind}' is not a payment mode kind.");
        }

        if (paymentModeId == Guid.Empty || accountId == Guid.Empty)
        {
            throw new InvalidOperationException("A tender names its payment mode and the account it posts to.");
        }

        // Refused rather than rounded: money handed over is a count of notes and paisa, and
        // quietly storing a different figure than the till sent would be the drawer's first
        // unexplained difference.
        if (decimal.Round(amount, Invoice.PosMoneyScale) != amount)
        {
            throw new InvalidOperationException($"A tender is whole paisa (at most {Invoice.PosMoneyScale} decimal places).");
        }

        return new InvoiceTender
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            PaymentModeId = paymentModeId,
            Kind = kind,
            AccountId = accountId,
            Amount = amount,
        };
    }
}
