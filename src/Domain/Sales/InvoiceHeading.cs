namespace ErpApp.Domain.Sales;

/// <summary>
/// The heading a printed invoice carries, decided by the seller and the bill (phase-62-status.md
/// Decision A). Phase 67 moved it here from the till's receipt command, because the ERP's PDF now
/// prints the same heading and two copies of one rule are two chances to disagree.
///
/// <para>The user chose VAT registration as the test (phase-67-status.md Decision A). The reference
/// product instead heads an approved ERP invoice "Estimate Bill" until the tenant is IRD-enabled, which
/// leaves a VAT-registered seller with no tax invoice to hand anyone.</para>
/// </summary>
public enum InvoiceHeading
{
    /// <summary>The seller is not VAT-registered, so no bill of theirs is a tax invoice.</summary>
    Invoice = 1,

    /// <summary>The full tax invoice, VAT Rules Rule 17 (Schedule 5).</summary>
    TaxInvoice = 2,

    /// <summary>VAT Rules Rule 18 (Schedule 6): permitted, retail, at most Rs 10,000. Decided at the
    /// sale and stored on it (<see cref="Invoice.IsAbbreviatedTaxInvoice"/>).</summary>
    AbbreviatedTaxInvoice = 3,
}

public static class InvoiceHeadings
{
    public static InvoiceHeading For(bool sellerIsVatRegistered, bool isAbbreviatedTaxInvoice) =>
        !sellerIsVatRegistered
            ? InvoiceHeading.Invoice
            : isAbbreviatedTaxInvoice ? InvoiceHeading.AbbreviatedTaxInvoice : InvoiceHeading.TaxInvoice;

    /// <summary>The English heading, as both the till receipt and the PDF print it.</summary>
    public static string English(InvoiceHeading heading) => heading switch
    {
        InvoiceHeading.TaxInvoice => "Tax Invoice",
        InvoiceHeading.AbbreviatedTaxInvoice => "Abbreviated Tax Invoice",
        _ => "Invoice",
    };

    /// <summary>The Nepali heading the till receipt already printed (phase 62), now on the PDF too.</summary>
    public static string Nepali(InvoiceHeading heading) => heading switch
    {
        InvoiceHeading.TaxInvoice => "कर बीजक",
        InvoiceHeading.AbbreviatedTaxInvoice => "संक्षिप्त कर बीजक",
        _ => "बीजक",
    };

    /// <summary>The credit note's Nepali heading, as the till's refund receipt prints it (phase 63).</summary>
    public const string CreditNoteNepali = "क्रेडिट नोट";
}

/// <summary>
/// How a counted copy left the system (phase-67-status.md Decision B). Every medium counts toward
/// "printed N times": the first copy out is the original whichever way it went, so an emailed
/// original and a paper original cannot both exist unmarked. The medium is recorded, not consulted.
/// </summary>
public enum PrintMedium
{
    /// <summary>The till's receipt (phases 62 and 63). Every row written before phase 67 is one.</summary>
    TillReceipt = 0,

    /// <summary>The ERP's PDF, from a document's Print button.</summary>
    Pdf = 1,

    /// <summary>The PDF attached to an email by the send job.</summary>
    Email = 2,
}
