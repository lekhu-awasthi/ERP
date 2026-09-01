namespace ErpApp.Domain.Purchasing;

/// <summary>
/// One historical Purchase Book line carried over from a prior system at cutover
/// (product-requirements.md FR-2.10, closing FR-9.4's "migrated" register variants).
///
/// <para><b>The same invariant as
/// <see cref="ErpApp.Domain.Sales.MigratedSalesRegisterEntry"/>, and it is the whole point of both
/// types</b> -- read that type's doc comment for the full statement. In short: no
/// <c>GlJournalEntry</c>, no <c>StockLedgerEntry</c>/<c>StockMovement</c>, no <c>Payment</c>, no
/// document number, no Draft/Approve/Void lifecycle, no approval queue, no lock-date gate, and no
/// presence in the live Purchase Register, VAT Summary, Annex 5, Annex 13 or TDS report. It exists
/// solely to be summed by <c>MigratedPurchaseRegisterQuery</c>.</para>
///
/// <para><b>Two tables rather than one, and this type is the argument</b> (Decision A, following
/// Phase 21b's Decision C reasoning verbatim). The two registers share exactly five fields -- date,
/// document code, party name, party PAN and the tax-exempt bucket -- and then diverge completely:
/// Sales has one taxable bucket plus four export columns, Purchase has three taxable value/VAT
/// pairs split Local/Import/Capital plus a customs declaration number. One table would carry eight
/// permanently-null columns for every row in either direction, which is the precise shape 21b
/// refused when it split ExportJob from ImportJob.</para>
///
/// <para><b>Unlike the Sales side there is no domain gap to make up for.</b> The live Purchase
/// Register can already populate every one of these columns from a real PurchaseBill's
/// <c>IsImport</c>/<c>ImportDocumentNo</c> (Phase 6) and per-line <c>ExpenditureClassification</c>
/// (Phase 8e) -- Phase 19 decision #3. Migrated rows carry the same buckets pre-split, because a
/// prior system's statutory register already printed them that way.</para>
/// </summary>
public sealed class MigratedPurchaseRegisterEntry
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    /// <summary>The prior system's own transaction date -- never derived from the clock.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The prior system's own bill number, copied verbatim. Unique per organization; that
    /// index is what makes an accidental second upload reject rather than double the tenant's
    /// statutory purchases.</summary>
    public string DocumentCode { get; private set; } = null!;

    /// <summary>Customs declaration ("Pragyapan Patra") number for an imported purchase.</summary>
    public string? ImportDeclarationNo { get; private set; }

    public string PartyName { get; private set; } = null!;

    public string? PartyPan { get; private set; }

    /// <summary>Set only when an existing Contact carried exactly this PAN at import time.</summary>
    public Guid? ContactId { get; private set; }

    public decimal TaxExemptValue { get; private set; }
    public decimal TaxableNonCapitalLocalValue { get; private set; }
    public decimal TaxableNonCapitalLocalVat { get; private set; }
    public decimal TaxableNonCapitalImportValue { get; private set; }
    public decimal TaxableNonCapitalImportVat { get; private set; }
    public decimal TaxableCapitalValue { get; private set; }
    public decimal TaxableCapitalVat { get; private set; }

    /// <summary>When the row was imported -- provenance only, never a business date.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    private MigratedPurchaseRegisterEntry()
    {
    }

    public static MigratedPurchaseRegisterEntry Create(
        Guid organizationId,
        DateOnly date,
        string documentCode,
        string? importDeclarationNo,
        string partyName,
        string? partyPan,
        Guid? contactId,
        decimal taxExemptValue,
        decimal taxableNonCapitalLocalValue,
        decimal taxableNonCapitalLocalVat,
        decimal taxableNonCapitalImportValue,
        decimal taxableNonCapitalImportVat,
        decimal taxableCapitalValue,
        decimal taxableCapitalVat,
        DateTimeOffset now)
    {
        return new MigratedPurchaseRegisterEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Date = date,
            DocumentCode = documentCode,
            ImportDeclarationNo = importDeclarationNo,
            PartyName = partyName,
            PartyPan = partyPan,
            ContactId = contactId,
            TaxExemptValue = taxExemptValue,
            TaxableNonCapitalLocalValue = taxableNonCapitalLocalValue,
            TaxableNonCapitalLocalVat = taxableNonCapitalLocalVat,
            TaxableNonCapitalImportValue = taxableNonCapitalImportValue,
            TaxableNonCapitalImportVat = taxableNonCapitalImportVat,
            TaxableCapitalValue = taxableCapitalValue,
            TaxableCapitalVat = taxableCapitalVat,
            CreatedAt = now,
        };
    }
}
