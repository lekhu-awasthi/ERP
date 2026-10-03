namespace ErpApp.Domain.Sales;

/// <summary>
/// Phase 63 -- one printing of a till refund's credit note. Append-only, exactly as
/// <see cref="InvoicePrint"/> is for the sale, and for the same rule: the Procedure Related to
/// Computerized Invoicing, 2072, §6 lets software print a bill once and requires every reprint to say
/// "copy of original" with how many times it has been printed. The procedure's text says
/// <i>invoices</i>; a credit note is the bill's statutory counterpart in the same billing records (the
/// IRD's credit note book, and the notes CBMS receives beside invoices), so it is held to the same
/// rule. That reading is the conservative one: it can only add a "copy" mark, never omit one
/// (phase-63-status.md Decision A).
///
/// <para><b>A sibling, not a generalised <see cref="InvoicePrint"/>.</b> Each print row carries a real
/// foreign key to the one document it counts, with the unique (organization, document, number) index
/// that makes the count a count. A polymorphic (type, id) row would give up the foreign key for one
/// shared table, and phase 18's polymorphic parents each needed their own reason; this has none
/// (Decision B).</para>
/// </summary>
public sealed class CreditNotePrint
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid CreditNoteId { get; private set; }

    /// <summary>1 for the original, 2 for the first copy, and so on.</summary>
    public int PrintNumber { get; private set; }

    public Guid PrintedByUserId { get; private set; }
    public DateTimeOffset PrintedAt { get; private set; }

    /// <summary>Phase 67 -- how this copy left the system; see <see cref="InvoicePrint.Medium"/>.</summary>
    public PrintMedium Medium { get; private set; }

    public bool IsCopy => PrintNumber > 1;

    private CreditNotePrint()
    {
    }

    /// <summary>Phase 67: the ERP's PDF and email write here too (one note, one count); only the till
    /// receipt stays till-only, as <see cref="InvoicePrint.Record"/> explains.</summary>
    public static CreditNotePrint Record(
        CreditNote creditNote, int printsSoFar, Guid printedByUserId, DateTimeOffset printedAt, PrintMedium medium)
    {
        ArgumentNullException.ThrowIfNull(creditNote);

        if (medium == PrintMedium.TillReceipt && creditNote.Channel != SalesChannel.Pos)
        {
            throw new InvalidOperationException(
                "Only a till refund prints a receipt; an ERP credit note is printed from its own page.");
        }

        if (creditNote.Status != CreditNoteStatus.Approved)
        {
            throw new InvalidOperationException(
                creditNote.Status == CreditNoteStatus.Void
                    ? $"Credit note {creditNote.Code} has been voided, so it is no longer a note to hand anyone."
                    : $"Credit note {creditNote.Code} is a draft. It has no number until it is approved, so it cannot be printed.");
        }

        if (printsSoFar < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(printsSoFar), "A print count is never negative.");
        }

        if (printedByUserId == Guid.Empty)
        {
            throw new InvalidOperationException("A print names who printed it.");
        }

        return new CreditNotePrint
        {
            Id = Guid.NewGuid(),
            OrganizationId = creditNote.OrganizationId,
            CreditNoteId = creditNote.Id,
            PrintNumber = printsSoFar + 1,
            PrintedByUserId = printedByUserId,
            PrintedAt = printedAt,
            Medium = medium,
        };
    }
}
