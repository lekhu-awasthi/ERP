using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Phase 73 (Decision A) -- the fixed layouts a <see cref="PrintingTemplate"/> chooses between. A layout is drawing
/// code, never data: the vendor's layouts share one settings shape key for key and differ only in how they are drawn
/// (phase-73-status.md § Live read 1), so ours carry nothing but this name.
///
/// <para><see cref="Standard"/> is member 0 on purpose: it is the frame every document printed before phase 73, so a
/// template row written before the column existed, and an organization with no template at all, keep printing
/// exactly what they printed. The column stores the member by <b>name</b>, so members are only ever appended.</para>
/// </summary>
public enum PrintLayout
{
    /// <summary>The centred organization block over a centred title, plain tables (phase 44's arrangement).</summary>
    Standard = 0,

    /// <summary>Every block ruled: an organization box, a party | details box, a gridded table, totals in a ruled
    /// column -- the familiar Nepali bill.</summary>
    Traditional = 1,

    /// <summary>Left-aligned, no logo, no rules but the table header's, smaller type.</summary>
    Minimal = 2,

    /// <summary>Phase 79 -- the organization flush left in light type with the logo opposite, a rule under each header
    /// block, the title and details as a label/value list opposite the party, a rule between every line of the table,
    /// and the total in words under the totals (the vendor's Modern, read live 2026-10-11).</summary>
    Modern = 3,

    /// <summary>Phase 79 -- a centred header over a rule, "Bill To" opposite the details, a fully gridded table, and
    /// one box below it holding the notes and signature beside the totals and the total in words (the vendor's
    /// Classic, read live 2026-10-11).</summary>
    Classic = 4,

    /// <summary>Phase 79 -- an 80 mm roll for a thermal printer, one column top to bottom, each line stacked as its
    /// name over its figures. Offered only for the types the vendor offers its Retail layout for
    /// (<see cref="PrintLayouts.RetailTypes"/>), and statutory-complete where the vendor's is not: both dates, the
    /// seller's and buyer's PAN and address, the copy mark.</summary>
    Retail = 5,
}

/// <summary>
/// Phase 79 -- which layouts each document type is offered, and what each layout has no room for. One table, read by
/// the template's own guard, by the save's and the preview's 400, by the applier, and (through
/// <c>GET /printing-templates/elements</c>) by the editor's radios and checkboxes, so no screen offers what the save
/// refuses (phase 73's lesson 3).
/// </summary>
public static class PrintLayouts
{
    /// <summary>The A4 layouts: every printable type is offered all of them, because the renderer never switches on a
    /// document type (phase 27b).</summary>
    public static readonly IReadOnlyList<PrintLayout> A4 =
        [PrintLayout.Standard, PrintLayout.Traditional, PrintLayout.Minimal, PrintLayout.Modern, PrintLayout.Classic];

    /// <summary>The types offered the 80 mm layout: the vendor's Retail is offered for exactly these three (phase-73
    /// § Live read 4, re-read 2026-10-11). A purchase document or a payment printed on a till roll is not a thing a
    /// counter does.</summary>
    public static readonly IReadOnlyList<DocumentType> RetailTypes =
        [DocumentType.Invoice, DocumentType.CreditNote, DocumentType.SalesOrder];

    /// <summary>The layouts this type may choose, in the order the editor lists them. Empty for a type with no
    /// printable record.</summary>
    public static IReadOnlyList<PrintLayout> OfferedFor(DocumentType documentType)
    {
        if (!DocumentMechanisms.Printable.Contains(documentType))
        {
            return [];
        }

        return RetailTypes.Contains(documentType) ? [.. A4, PrintLayout.Retail] : A4;
    }

    public static bool IsOffered(PrintLayout layout, DocumentType documentType) => OfferedFor(documentType).Contains(layout);

    /// <summary>
    /// The elements a layout never draws, whatever the template says: Minimal has no logo (phase 73), and the till roll
    /// has neither a logo nor a signature line (the vendor's Retail has no section for either). Never a statutory
    /// element -- <c>PrintLayoutsTests</c> holds every layout to that for every type it is offered to, so no layout is
    /// a way round <see cref="PrintElements.Locked"/>.
    /// </summary>
    public static IReadOnlyList<PrintElement> Omits(PrintLayout layout) => layout switch
    {
        PrintLayout.Minimal => [PrintElement.OrganizationLogo],
        PrintLayout.Retail => [PrintElement.OrganizationLogo, PrintElement.SignatureLine],
        _ => [],
    };
}
