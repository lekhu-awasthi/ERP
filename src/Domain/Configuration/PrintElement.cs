using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Phase 73 (Decision B) -- one thing a <see cref="PrintingTemplate"/> may hide. A template stores the members it
/// hides, never the ones it shows, so an empty set is "everything", which is what every template written before
/// this phase and every organization with no template at all print.
///
/// <para>Stored by <b>name</b> (a delimited string, as <c>CustomFieldDefinition.ApplicableDocumentTypes</c> is), so
/// inserting a member never re-reads an existing row as a different element (phase 26a's ordinal-mapping lesson).</para>
///
/// <para>Not here, deliberately: the title, the number, the dates, the buyer's PAN, the copy mark and who printed a
/// counted copy. Those are what a bill <i>is</i> (phases 67 and 71), so no template is offered a way to hide them.</para>
/// </summary>
public enum PrintElement
{
    OrganizationLogo,
    OrganizationAddress,
    OrganizationPhone,
    OrganizationEmail,
    OrganizationPan,
    OrganizationWebsite,

    /// <summary>Phase 75 -- the document's location, name and address, under the organization's own address. Printed
    /// only when the organization has more than one location, so a one-branch tenant never sees "HeadOffice" on a
    /// bill. Never locked: Schedule 5 asks for the seller's address, which is the organization's registered one.</summary>
    Location,
    PartyAddress,
    Reference,
    ItemCode,
    HsCode,
    Unit,
    DiscountColumn,
    VatColumn,
    AmountInWords,
    Notes,
    Terms,
    SignatureLine,
}

/// <summary>
/// Phase 73 -- which <see cref="PrintElement"/>s a document type prints at all (and so may hide), and which of those
/// it may not hide. The print handler is the other half of this table: each builder prints exactly these, which
/// <c>PrintElementsTests</c> pins per family.
/// </summary>
public static class PrintElements
{
    /// <summary>The tax documents (VAT Rules 17, 18 and 20). Phase 71's <c>TaxDocuments</c> reads this list, so the
    /// bills whose dates print in BS and the bills whose seller and buyer cannot be hidden are one set.</summary>
    public static readonly IReadOnlyList<DocumentType> TaxDocumentTypes =
        [DocumentType.Invoice, DocumentType.CreditNote, DocumentType.DebitNote];

    /// <summary>The nine types printed through one product table (quantity, rate, discount, VAT, amount).</summary>
    public static readonly IReadOnlyList<DocumentType> ProductLineTypes =
    [
        DocumentType.Quotation, DocumentType.SalesOrder, DocumentType.Invoice, DocumentType.CreditNote,
        DocumentType.PurchaseOrder, DocumentType.PurchaseBill, DocumentType.DebitNote,
        DocumentType.DeliveryNote, DocumentType.GoodsReceivedNote,
    ];

    /// <summary>Types that name a product on a line but have no product table: their lines carry the item code
    /// inside the product's label, and nothing else a template can switch.</summary>
    private static readonly IReadOnlyList<DocumentType> OtherProductTypes =
    [
        DocumentType.WarehouseTransfer, DocumentType.InventoryAdjustment,
        DocumentType.ProductionOrder, DocumentType.ProductionJournal,
    ];

    /// <summary>The product-line types plus the two that print a contact without a product table.</summary>
    private static readonly IReadOnlyList<DocumentType> PartyTypes =
        [.. ProductLineTypes, DocumentType.Payment, DocumentType.Expense];

    /// <summary>The types whose print carries a terms block (the document has a Terms field and prints it).</summary>
    private static readonly IReadOnlyList<DocumentType> TermsTypes =
    [
        DocumentType.Quotation, DocumentType.SalesOrder, DocumentType.Invoice, DocumentType.CreditNote,
        DocumentType.PurchaseOrder, DocumentType.DeliveryNote,
    ];

    /// <summary>The types whose print carries a notes block.</summary>
    private static readonly IReadOnlyList<DocumentType> NotesTypes =
        [DocumentType.Expense, DocumentType.ProductionOrder, DocumentType.ProductionJournal];

    private static readonly IReadOnlyList<PrintElement> Everywhere =
    [
        PrintElement.OrganizationLogo, PrintElement.OrganizationAddress, PrintElement.OrganizationPhone,
        PrintElement.OrganizationEmail, PrintElement.OrganizationPan, PrintElement.OrganizationWebsite,
        PrintElement.Location, PrintElement.SignatureLine,
    ];

    /// <summary>VAT Rules Schedule 5 asks a tax invoice for the seller's name, address and PAN and the buyer's name,
    /// address and PAN (phase 62 § Rule 17). The names and the buyer's PAN are not elements at all; these three are,
    /// for every other type.</summary>
    private static readonly IReadOnlyList<PrintElement> StatutoryOnTaxDocuments =
        [PrintElement.OrganizationAddress, PrintElement.OrganizationPan, PrintElement.PartyAddress];

    public static bool IsTaxDocument(DocumentType documentType) => TaxDocumentTypes.Contains(documentType);

    /// <summary>The elements this type prints, in the order the editor lists them. Empty for a type that has no
    /// printable record.</summary>
    public static IReadOnlyList<PrintElement> For(DocumentType documentType)
    {
        if (!DocumentMechanisms.Printable.Contains(documentType))
        {
            return [];
        }

        var productLine = ProductLineTypes.Contains(documentType);
        var applicable = new List<PrintElement>(Everywhere);

        if (PartyTypes.Contains(documentType))
        {
            applicable.Add(PrintElement.PartyAddress);
        }

        // The expense print has no reference line (it has its own supplier invoice reference field).
        if (documentType != DocumentType.Expense)
        {
            applicable.Add(PrintElement.Reference);
        }

        if (productLine || OtherProductTypes.Contains(documentType))
        {
            applicable.Add(PrintElement.ItemCode);
        }

        if (productLine)
        {
            applicable.AddRange([PrintElement.HsCode, PrintElement.Unit, PrintElement.DiscountColumn, PrintElement.VatColumn]);
        }

        if (productLine || documentType is DocumentType.Payment or DocumentType.Expense)
        {
            applicable.Add(PrintElement.AmountInWords);
        }

        if (NotesTypes.Contains(documentType))
        {
            applicable.Add(PrintElement.Notes);
        }

        if (TermsTypes.Contains(documentType))
        {
            applicable.Add(PrintElement.Terms);
        }

        return applicable.OrderBy(x => x).ToList();
    }

    /// <summary>The applicable elements this type may not hide.</summary>
    public static IReadOnlyList<PrintElement> Locked(DocumentType documentType) =>
        IsTaxDocument(documentType)
            ? StatutoryOnTaxDocuments.Where(x => For(documentType).Contains(x)).ToList()
            : [];

    /// <summary>Whether a template for this type may hide this element: it must print it, and not be required to.</summary>
    public static bool CanHide(DocumentType documentType, PrintElement element) =>
        For(documentType).Contains(element) && !Locked(documentType).Contains(element);
}
