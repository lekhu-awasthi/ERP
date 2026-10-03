namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 62 -- one printing of a till sale's receipt. Append-only: a row is written for every print
/// and never changed.
///
/// <para><b>Why the count is stored, and stored here.</b> The Procedure Related to Computerized
/// Invoicing, 2072 (§6) lets software print an invoice <b>once</b>. Any reprint must carry a visible
/// <i>"Copy of Original"</i> mark <b>and state how many times the invoice has been printed</b> -- its
/// own example is that the fourth copy says the invoice has been printed five times. A number that
/// must be on the paper and must be right across two tills and two cashiers cannot live in a
/// browser, so the server counts. A log rather than a counter column on <see cref="Invoice"/>
/// because the same rule's materialised view wants who printed and when, and because a row per print
/// is the append-only fact phase 46 prefers to a figure overwritten in place
/// (phase-62-status.md Decision B).</para>
///
/// <para><b>The number is the order of printing</b>, starting at 1 for the original. A unique index
/// on (organization, invoice, number) is what makes it a count: two tills reprinting the same bill
/// in the same instant cannot both be "copy 2", and the loser is refused rather than renumbered.</para>
/// </summary>
public sealed class InvoicePrint
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }

    /// <summary>1 for the original, 2 for the first copy, and so on.</summary>
    public int PrintNumber { get; private set; }

    public Guid PrintedByUserId { get; private set; }
    public DateTimeOffset PrintedAt { get; private set; }

    /// <summary>Phase 67 -- how this copy left the system. Recorded, never consulted: every medium
    /// counts toward the same number (phase-67-status.md Decision B).</summary>
    public PrintMedium Medium { get; private set; }

    /// <summary>True for every print after the first: the one the rule says must be marked.</summary>
    public bool IsCopy => PrintNumber > 1;

    private InvoicePrint()
    {
    }

    /// <summary>
    /// Records the next print of <paramref name="invoice"/>. <paramref name="printsSoFar"/> is how
    /// many rows already exist; the caller reads it, and the unique index refuses a stale read.
    ///
    /// <para>Phase 67: the ERP's PDF and its email attachment write here too, so a till sale printed at
    /// the till and again from the invoice page is one count (Decision D). Only the till receipt is
    /// still till-only, because it is a receipt's layout, not a statutory rule.</para>
    /// </summary>
    public static InvoicePrint Record(
        Invoice invoice, int printsSoFar, Guid printedByUserId, DateTimeOffset printedAt, PrintMedium medium)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (medium == PrintMedium.TillReceipt && invoice.Channel != SalesChannel.Pos)
        {
            throw new InvalidOperationException(
                "Only a till sale prints a receipt; an ERP invoice is printed from its own page.");
        }

        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new InvalidOperationException(
                invoice.Status == InvoiceStatus.Void
                    ? $"Invoice {invoice.Code} has been voided, so it is no longer a bill to hand anyone."
                    : $"Invoice {invoice.Code} is a draft. It has no number until it is approved, so it cannot be printed.");
        }

        if (printsSoFar < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(printsSoFar), "A print count is never negative.");
        }

        if (printedByUserId == Guid.Empty)
        {
            throw new InvalidOperationException("A print names who printed it.");
        }

        return new InvoicePrint
        {
            Id = Guid.NewGuid(),
            OrganizationId = invoice.OrganizationId,
            InvoiceId = invoice.Id,
            PrintNumber = printsSoFar + 1,
            PrintedByUserId = printedByUserId,
            PrintedAt = printedAt,
            Medium = medium,
        };
    }
}
